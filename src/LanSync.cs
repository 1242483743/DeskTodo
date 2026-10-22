using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopTodo
{
    [DataContract]
    internal sealed class LanPeer
    {
        [DataMember] public string InstanceId;
        [DataMember] public string DisplayName;
        [DataMember] public string MachineName;
        [DataMember] public int Port;
        [DataMember] public string Version;
        internal string Address;
        internal string LocalAddress;
        internal string Alias;
        public string DisplayTitle { get { return String.IsNullOrWhiteSpace(Alias) ? DisplayName : Alias; } }
        public string DeviceName { get { return MachineName; } }
        public override string ToString() { return DisplayTitle + "  ·  " + MachineName; }
    }

    [DataContract]
    internal sealed class LanGroupInfo
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember(EmitDefaultValue = false)] public bool Pinned;
        public string DisplayName { get { return Name; } }
        public override string ToString() { return Name; }
    }

    [DataContract]
    internal sealed class LanTaskInfo
    {
        [DataMember] public string Id;
        [DataMember] public string Text;
        [DataMember] public string GroupId;
        [DataMember] public bool Completed;
        [DataMember] public long CreatedAt;
        [DataMember] public long CompletedAt;
        [DataMember(EmitDefaultValue = false)] public bool Pinned;
        [DataMember(EmitDefaultValue = false)] public int SortOrder;
        [DataMember(EmitDefaultValue = false)] public int ReminderIntervalMinutes;
        [DataMember(EmitDefaultValue = false)] public int DailyReminderMinutesPlusOne;
        [DataMember(EmitDefaultValue = false)] public bool ReminderManaged;
        public override string ToString() { return (Completed ? "✓  " : "○  ") + Text.Replace('\r', ' ').Replace('\n', ' '); }
    }

    [DataContract]
    internal sealed class LanSnapshot
    {
        [DataMember] public long Revision;
        [DataMember] public string Title;
        [DataMember(EmitDefaultValue = false)] public string PendingSort;
        [DataMember(EmitDefaultValue = false)] public bool DisableGroups;
        [DataMember] public List<LanGroupInfo> Groups = new List<LanGroupInfo>();
        [DataMember] public List<LanTaskInfo> Tasks = new List<LanTaskInfo>();
    }

    [DataContract]
    internal sealed class LanRequest
    {
        [DataMember] public string Operation;
        [DataMember] public string AccessCode;
        [DataMember] public string ItemId;
        [DataMember] public string Text;
        [DataMember] public string GroupId;
        [DataMember] public bool Completed;
        // Zero means a legacy/full read; plus one also represents revision zero.
        [DataMember(EmitDefaultValue = false)] public long KnownRevisionPlusOne;
        [DataMember(EmitDefaultValue = false)] public long ExpectedRevisionPlusOne;
        [DataMember(EmitDefaultValue = false)] public string TargetId;
        [DataMember(EmitDefaultValue = false)] public bool After;
        [DataMember(EmitDefaultValue = false)] public int ReminderIntervalMinutes;
        [DataMember(EmitDefaultValue = false)] public int DailyReminderMinutesPlusOne;
        [DataMember(EmitDefaultValue = false)] public bool ReminderManaged;
    }

    [DataContract]
    internal sealed class LanResponse
    {
        [DataMember] public bool Success;
        [DataMember] public string Error;
        [DataMember] public LanSnapshot Snapshot;
        internal static LanResponse Fail(string message) { return new LanResponse { Success = false, Error = message }; }
    }

    internal static class LanJson
    {
        internal static string Write<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        internal static T Read<T>(string value)
        {
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(value ?? "")))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }

    internal sealed class LanService : IDisposable
    {
        internal const int DiscoveryPort = 32741;
        internal const string DiscoveryMessage = "DESKTODO_DISCOVER_V1";
        private readonly Func<LanPeer> peerFactory;
        private readonly Func<LanRequest, LanResponse> requestHandler;
        private readonly Action<string> log;
        private readonly TcpListener tcp;
        private readonly UdpClient udp;
        private readonly Thread tcpThread, udpThread;
        private volatile bool disposed;
        internal int Port { get; private set; }

        internal LanService(Func<LanPeer> peerFactory, Func<LanRequest, LanResponse> requestHandler, Action<string> log, int discoveryPort = DiscoveryPort)
        {
            this.peerFactory = peerFactory; this.requestHandler = requestHandler; this.log = log;
            // Port zero is used by isolated QA fixtures, never by normal sharing.
            // Keep those fixtures off external interfaces while retaining real TCP.
            IPAddress binding = discoveryPort == 0 ? IPAddress.Loopback : IPAddress.Any;
            try
            {
                tcp = new TcpListener(binding, 0); tcp.Start(8);
                Port = ((IPEndPoint)tcp.LocalEndpoint).Port;
                udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(binding, discoveryPort));
                tcpThread = new Thread(AcceptLoop) { IsBackground = true, Name = "DeskTodo LAN commands" };
                udpThread = new Thread(DiscoveryLoop) { IsBackground = true, Name = "DeskTodo LAN discovery" };
                tcpThread.Start(); udpThread.Start();
            }
            catch { Dispose(); throw; }
        }

        private void DiscoveryLoop()
        {
            while (!disposed)
            {
                try
                {
                    IPEndPoint source = new IPEndPoint(IPAddress.Any, 0);
                    byte[] bytes = udp.Receive(ref source);
                    if (Encoding.UTF8.GetString(bytes) != DiscoveryMessage) continue;
                    LanPeer peer = peerFactory(); peer.Port = Port;
                    byte[] reply = Encoding.UTF8.GetBytes(LanJson.Write(peer));
                    LanRoute replyRoute = LanRouting.FindRoute(source.Address, LanRouting.GetRoutes());
                    if (replyRoute == null) continue;
                    LanRouting.PinInterface(udp.Client, replyRoute.InterfaceIndex);
                    udp.Send(reply, reply.Length, source);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { if (!disposed) Thread.Sleep(100); }
                catch (Exception e) { RecordFailure("LAN discovery", e); }
            }
        }

        private void AcceptLoop()
        {
            while (!disposed)
            {
                try
                {
                    TcpClient client = tcp.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate { Handle(client); });
                }
                catch (SocketException) { if (!disposed) Thread.Sleep(100); }
                catch (ObjectDisposedException) { break; }
                catch (Exception e) { RecordFailure("LAN accept", e); }
            }
        }

        private void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 4000; client.SendTimeout = 4000;
                    IPEndPoint localEndpoint = client.Client.LocalEndPoint as IPEndPoint;
                    LanRoute localRoute = localEndpoint == null ? null : LanRouting.GetRoutes().FirstOrDefault(r => r.LocalAddress.Equals(localEndpoint.Address));
                    if (localRoute != null) LanRouting.PinInterface(client.Client, localRoute.InterfaceIndex);
                    using (NetworkStream network = client.GetStream())
                    using (StreamReader reader = new StreamReader(network, Encoding.UTF8, false, 4096, true))
                    using (StreamWriter writer = new StreamWriter(network, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                    {
                        string line = reader.ReadLine();
                        if (String.IsNullOrEmpty(line) || line.Length > 1024 * 1024) { writer.WriteLine(LanJson.Write(LanResponse.Fail("请求无效。"))); return; }
                        LanRequest request = LanJson.Read<LanRequest>(line);
                        LanResponse response = requestHandler(request) ?? LanResponse.Fail("没有收到响应。");
                        writer.WriteLine(LanJson.Write(response));
                    }
                }
                catch (Exception e)
                {
                    RecordFailure("LAN request", e);
                }
            }
        }

        internal static List<LanPeer> Discover(int waitMilliseconds)
        {
            List<LanRoute> routes = LanRouting.GetRoutes(); routes.Add(LanRoute.Loopback());
            return LanRouting.Discover(Math.Max(1600, waitMilliseconds), routes, DiscoveryPort);
        }

        internal static LanResponse Send(LanPeer peer, LanRequest request)
        {
            if (peer == null) return LanResponse.Fail("请先选择一位用户。");
            try
            {
                IPAddress destination;
                if (!IPAddress.TryParse(peer.Address, out destination)) return LanResponse.Fail("用户地址无效，请重新扫描。");
                LanRoute route = LanRouting.FindRoute(destination, LanRouting.GetRoutes(), peer.LocalAddress);
                using (TcpClient client = route == null ? new TcpClient() : new TcpClient(new IPEndPoint(route.LocalAddress, 0)))
                {
                    if (route != null) LanRouting.PinInterface(client.Client, route.InterfaceIndex);
                    IAsyncResult connecting = client.BeginConnect(peer.Address, peer.Port, null, null);
                    using (connecting.AsyncWaitHandle)
                    {
                        if (!connecting.AsyncWaitHandle.WaitOne(2500)) return LanResponse.Fail("连接超时。请确认对方开启共享；若启用 TUN 严格路由，请在代理中允许局域网直连。");
                        client.EndConnect(connecting);
                    }
                    client.ReceiveTimeout = 3500; client.SendTimeout = 3500;
                    using (NetworkStream network = client.GetStream())
                    using (StreamReader reader = new StreamReader(network, Encoding.UTF8, false, 4096, true))
                    using (StreamWriter writer = new StreamWriter(network, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                    {
                        writer.WriteLine(LanJson.Write(request));
                        string line = reader.ReadLine();
                        return String.IsNullOrEmpty(line) ? LanResponse.Fail("对方没有返回数据。") : LanJson.Read<LanResponse>(line);
                    }
                }
            }
            catch (Exception e) { return LanResponse.Fail("连接失败：" + e.Message); }
        }

        private void RecordFailure(string operation, Exception error)
        { try { if (log != null) log(operation + ": " + error); } catch { } }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { udp.Close(); } catch { }
            try { tcp.Stop(); } catch { }
        }
    }

    internal sealed class LanAdminWindowLegacy : IDisposable
    {
        private readonly Window window;
        private readonly ListBox users, tasks;
        private readonly TextBox code, taskText;
        private readonly ComboBox groupSelect;
        private readonly CheckBox completed;
        private readonly TextBlock status;
        private readonly Button connect;
        private readonly DispatcherTimer refreshTimer;
        private List<LanPeer> peers = new List<LanPeer>();
        private LanPeer selectedPeer;
        private LanSnapshot snapshot;
        private long revision = -1;
        private bool disposed, working;
        internal Window Window { get { return window; } }
        internal Button ConnectButton { get { return connect; } }
        internal CheckBox CompletedCheckBox { get { return completed; } }
        internal TextBlock StatusText { get { return status; } }

        internal LanAdminWindowLegacy(AppState appearance)
        {
            ThemePalette palette = ThemeCatalog.Get(appearance.ThemeId);
            string[] colors = appearance.DarkMode ? palette.Dark : palette.Light;
            Brush card = Solid(colors[0]), text = Solid(colors[1]), muted = Solid(colors[2]), line = Solid(colors[3]), input = Solid(colors[4]), accent = Solid(colors[5]);
            Brush accentText = Solid(ThemeCatalog.AccentForeground(colors[5]));
            window = new Window { Title = "DeskTodo 局域网管理", Width = 780, Height = 560, MinWidth = 680, MinHeight = 460, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = card, Foreground = text, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"), FontSize = 13, ShowInTaskbar = true, Icon = UiAssets.ApplicationIcon };
            window.Resources.MergedDictionaries.Add(AdminResources(colors));
            Grid root = new Grid { Margin = new Thickness(24), Background = card };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock title = new TextBlock { Text = "局域网待办管理", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = text };
            root.Children.Add(title);
            Grid body = new Grid { Margin = new Thickness(0, 20, 0, 14) }; Grid.SetRow(body, 1);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid left = new Grid(); Grid.SetColumn(left, 0);
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition());
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Button discover = Button("扫描局域网用户", accent, accentText); discover.Margin = new Thickness(0); discover.Click += delegate { Discover(); }; left.Children.Add(discover);
            users = new ListBox { Margin = new Thickness(0, 10, 0, 10), MinHeight = 80, Background = input, Foreground = text, BorderBrush = line, Padding = new Thickness(6), Style = window.Resources["AdminList"] as Style };
            users.SelectionChanged += delegate { selectedPeer = users.SelectedItem as LanPeer; revision = -1; status.Text = selectedPeer == null ? "请选择用户" : "请输入该用户设置页显示的管理口令"; };
            Grid.SetRow(users, 1); left.Children.Add(users);
            TextBlock codeLabel = new TextBlock { Text = "管理口令", Foreground = muted, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(codeLabel, 2); left.Children.Add(codeLabel);
            code = new TextBox { Height = 36, Padding = new Thickness(9, 6, 9, 6), Background = input, Foreground = text, BorderBrush = line, CharacterCasing = CharacterCasing.Upper, MaxLength = 16, Style = window.Resources["AdminInput"] as Style };
            Grid.SetRow(code, 3); left.Children.Add(code);
            connect = Button("连接并读取", input, text); connect.Margin = new Thickness(0, 10, 0, 0); connect.Click += delegate { RefreshRemote(true); }; Grid.SetRow(connect, 4); left.Children.Add(connect);
            body.Children.Add(left);
            Grid right = new Grid(); Grid.SetColumn(right, 2); right.RowDefinitions.Add(new RowDefinition()); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tasks = new ListBox { Background = input, Foreground = text, BorderBrush = line, Padding = new Thickness(6), Style = window.Resources["AdminList"] as Style };
            tasks.SelectionChanged += delegate { SelectTask(); }; right.Children.Add(tasks);
            Grid editor = new Grid { Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(editor, 1);
            editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editor.ColumnDefinitions.Add(new ColumnDefinition()); editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            taskText = new TextBox { Height = 62, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 500, Padding = new Thickness(9), Background = input, Foreground = text, BorderBrush = line, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Style = window.Resources["AdminInput"] as Style };
            editor.Children.Add(taskText);
            StackPanel options = new StackPanel { Margin = new Thickness(10, 0, 0, 0) }; Grid.SetColumn(options, 1);
            groupSelect = new ComboBox { Height = 34, Background = input, Foreground = text, BorderBrush = line, Style = window.Resources["AdminCombo"] as Style }; options.Children.Add(groupSelect);
            completed = new CheckBox { Content = "已完成", Margin = new Thickness(2, 10, 0, 0), Foreground = text, Style = window.Resources["AdminCheck"] as Style }; options.Children.Add(completed); editor.Children.Add(options);
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) }; Grid.SetRow(actions, 1); Grid.SetColumnSpan(actions, 2);
            Button add = Button("新增", input, text), save = Button("保存修改", accent, accentText), delete = Button("删除", input, text);
            add.Click += delegate { SendChange("add"); }; save.Click += delegate { SendChange("update"); }; delete.Click += delegate { SendChange("delete"); };
            actions.Children.Add(add); actions.Children.Add(save); actions.Children.Add(delete); editor.Children.Add(actions);
            right.Children.Add(editor); body.Children.Add(right); root.Children.Add(body);
            status = new TextBlock { Text = "先扫描局域网内已开启共享的 DeskTodo", Foreground = muted, TextWrapping = TextWrapping.Wrap }; Grid.SetRow(status, 2); root.Children.Add(status);
            window.Content = root;
            window.Closed += delegate { Dispose(); };
            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            refreshTimer.Tick += delegate { if (selectedPeer != null && snapshot != null) RefreshRemote(false); };
            refreshTimer.Start();
        }

        internal static ResourceDictionary AdminResources(string[] colors)
        {
            string xaml =
                "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:local='clr-namespace:DesktopTodo;assembly=" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + "'>" +
                "<SolidColorBrush x:Key='AdminCard' Color='" + colors[0] + "'/><SolidColorBrush x:Key='AdminText' Color='" + colors[1] + "'/><SolidColorBrush x:Key='AdminMuted' Color='" + colors[2] + "'/><SolidColorBrush x:Key='AdminLine' Color='" + colors[3] + "'/><SolidColorBrush x:Key='AdminInputBrush' Color='" + colors[4] + "'/><SolidColorBrush x:Key='AdminAccent' Color='" + colors[5] + "'/>" +
                "<SolidColorBrush x:Key='AdminAccentText' Color='" + ThemeCatalog.AccentForeground(colors[5]) + "'/>" +
                "<Style TargetType='ScrollBar'><Setter Property='Width' Value='6'/><Setter Property='MinWidth' Value='0'/><Setter Property='Opacity' Value='.65'/><Setter Property='Focusable' Value='False'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='Vertical'><Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb MinHeight='24' Focusable='False'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='{StaticResource AdminMuted}' CornerRadius='3'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></ControlTemplate></Setter.Value></Setter><Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='1'/></Trigger></Style.Triggers></Style>" +
                "<Style x:Key='AdminButton' TargetType='Button'><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='Surface' Background='{TemplateBinding Background}' CornerRadius='9' Padding='{TemplateBinding Padding}'><local:InkContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Opacity' Value='.86'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Opacity' Value='.68'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>" +
                "<Style x:Key='AdminInput' TargetType='TextBox'><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='HorizontalScrollBarVisibility' Value='Hidden'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'><Grid UseLayoutRounding='False' SnapsToDevicePixels='False'><Border Background='{TemplateBinding Background}' CornerRadius='9'/><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' ClipToBounds='True'/><local:InputOutline x:Name='Surface' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1.5' Margin='1' CornerRadius='9' UseLayoutRounding='False' SnapsToDevicePixels='False' IsHitTestVisible='False'/></Grid><ControlTemplate.Triggers><Trigger Property='AcceptsReturn' Value='True'><Setter TargetName='PART_ContentHost' Property='VerticalAlignment' Value='Stretch'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='{StaticResource AdminAccent}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>" +
                "<Style x:Key='AdminSwitch' TargetType='CheckBox'><Setter Property='Foreground' Value='{StaticResource AdminText}'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='MinHeight' Value='40'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><local:InkContentPresenter VerticalAlignment='Center' Margin='0,0,12,0'/><Grid Grid.Column='1' Width='36' Height='20' VerticalAlignment='Center' SnapsToDevicePixels='False'><Border Background='{StaticResource AdminLine}' CornerRadius='10'/><Border x:Name='ActiveTrack' Background='{StaticResource AdminAccent}' CornerRadius='10' Opacity='0'/><Ellipse x:Name='Knob' Width='16' Height='16' Fill='{StaticResource AdminCard}' HorizontalAlignment='Left' VerticalAlignment='Center' Margin='2'><Ellipse.RenderTransform><TranslateTransform/></Ellipse.RenderTransform></Ellipse></Grid></Grid><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Knob' Property='Fill' Value='{StaticResource AdminAccentText}'/><Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='Knob' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='16' Duration='0:0:0.18'/><DoubleAnimation Storyboard.TargetName='ActiveTrack' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.18'/></Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='Knob' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='0' Duration='0:0:0.18'/><DoubleAnimation Storyboard.TargetName='ActiveTrack' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.18'/></Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>" +
                "<Style x:Key='AdminListItem' TargetType='ListBoxItem'><Setter Property='Padding' Value='9,7'/><Setter Property='Margin' Value='0,0,0,3'/><Setter Property='Foreground' Value='{StaticResource AdminText}'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='Choice' Background='Transparent' CornerRadius='7' Padding='{TemplateBinding Padding}'><local:InkContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Choice' Property='Background' Value='{StaticResource AdminCard}'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='Choice' Property='Background' Value='{StaticResource AdminAccent}'/><Setter Property='Foreground' Value='{StaticResource AdminAccentText}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>" +
                "<Style x:Key='AdminList' TargetType='ListBox'><Setter Property='ItemContainerStyle' Value='{StaticResource AdminListItem}'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBox'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='11' Padding='{TemplateBinding Padding}'><ScrollViewer VerticalScrollBarVisibility='Auto'><ItemsPresenter/></ScrollViewer></Border></ControlTemplate></Setter.Value></Setter></Style>" +
                "<Style x:Key='AdminCheck' TargetType='CheckBox'><Setter Property='Foreground' Value='{StaticResource AdminText}'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><Border x:Name='Box' Width='18' Height='18' CornerRadius='5' BorderBrush='{StaticResource AdminMuted}' BorderThickness='1.5' Background='{StaticResource AdminCard}' VerticalAlignment='Center'><Path x:Name='Tick' Data='M 0,4 L 3,7 L 10,0' Width='12' Height='9' Stretch='Uniform' HorizontalAlignment='Center' VerticalAlignment='Center' Stroke='{StaticResource AdminAccentText}' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Visibility='Collapsed'/></Border><local:InkContentPresenter Grid.Column='1' Margin='8,0,0,0' VerticalAlignment='Center'/></Grid><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='{StaticResource AdminAccent}'/><Setter TargetName='Box' Property='BorderBrush' Value='{StaticResource AdminAccent}'/><Setter TargetName='Tick' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='{StaticResource AdminAccent}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>" +
                "<Style x:Key='AdminComboItem' TargetType='ComboBoxItem' BasedOn='{StaticResource AdminListItem}'/>" +
                "<Style x:Key='AdminCombo' TargetType='ComboBox'><Setter Property='ItemContainerStyle' Value='{StaticResource AdminComboItem}'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'><Grid><ToggleButton Focusable='False' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' Background='Transparent' BorderThickness='0'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border x:Name='Surface' Background='{StaticResource AdminInputBrush}' BorderBrush='{StaticResource AdminLine}' BorderThickness='1' CornerRadius='8'><Grid><local:InkContentPresenter Margin='9,0,28,0' VerticalAlignment='Center' TextElement.Foreground='{StaticResource AdminText}' Content='{Binding SelectedItem, RelativeSource={RelativeSource AncestorType=ComboBox}}' ContentTemplate='{Binding ItemTemplate, RelativeSource={RelativeSource AncestorType=ComboBox}}'/><Path Data='M 2,3 L 6,7 L 10,3' Stroke='{StaticResource AdminMuted}' Width='12' Height='9' Stretch='None' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,9,0'/></Grid></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='{StaticResource AdminAccent}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template></ToggleButton><Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True'><Border Background='{StaticResource AdminCard}' BorderBrush='{StaticResource AdminLine}' BorderThickness='1' CornerRadius='8' Padding='4' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'><ScrollViewer><StackPanel IsItemsHost='True'/></ScrollViewer></Border></Popup></Grid></ControlTemplate></Setter.Value></Setter></Style>" +
                "</ResourceDictionary>";
            return (ResourceDictionary)XamlReader.Parse(xaml);
        }
        private static Brush Solid(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        private Button Button(string label, Brush background, Brush foreground)
        {
            return new Button { Content = label, Height = 36, MinWidth = 82, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(5, 0, 0, 0), Background = background, Foreground = foreground, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, FocusVisualStyle = null, Style = window.Resources["AdminButton"] as Style };
        }
        internal void Show() { if (disposed) return; window.Show(); window.Activate(); if (peers.Count == 0) Discover(); }

        private void Discover()
        {
            if (working) return; working = true; status.Text = "正在扫描…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<LanPeer> found = LanService.Discover(700);
                window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (disposed) return; working = false; peers = found; users.ItemsSource = null; users.ItemsSource = peers;
                    status.Text = found.Count == 0 ? "没有发现用户。请确认对方已在设置中开启“允许局域网管理”，并允许专用网络访问。" : "发现 " + found.Count + " 位用户";
                }));
            });
        }

        private void RefreshRemote(bool announce)
        {
            if (working || selectedPeer == null || String.IsNullOrWhiteSpace(code.Text)) return;
            working = true; string access = code.Text.Trim(); LanPeer peer = selectedPeer;
            if (announce) status.Text = "正在连接 " + peer.DisplayName + "…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                LanResponse response = LanService.Send(peer, new LanRequest { Operation = "snapshot", AccessCode = access });
                window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (disposed) return; working = false;
                    if (!response.Success) { status.Text = response.Error; snapshot = null; tasks.ItemsSource = null; return; }
                    ApplySnapshot(response.Snapshot); status.Text = "已连接 · 数据每约 1 秒自动刷新";
                }));
            });
        }

        private void ApplySnapshot(LanSnapshot value)
        {
            if (value == null || value.Revision == revision) return;
            string selectedId = (tasks.SelectedItem as LanTaskInfo) == null ? null : ((LanTaskInfo)tasks.SelectedItem).Id;
            snapshot = value; revision = value.Revision;
            tasks.ItemsSource = null; tasks.ItemsSource = value.Tasks.OrderBy(t => t.Completed).ThenBy(t => t.CreatedAt).ToList();
            groupSelect.Items.Clear(); groupSelect.Items.Add(new LanGroupInfo { Id = null, Name = "未分组" });
            foreach (LanGroupInfo group in value.Groups) groupSelect.Items.Add(group);
            groupSelect.SelectedIndex = 0;
            if (selectedId != null) tasks.SelectedItem = value.Tasks.FirstOrDefault(t => t.Id == selectedId);
        }

        private void SelectTask()
        {
            LanTaskInfo item = tasks.SelectedItem as LanTaskInfo;
            if (item == null) return;
            taskText.Text = item.Text; completed.IsChecked = item.Completed;
            groupSelect.SelectedItem = groupSelect.Items.Cast<LanGroupInfo>().FirstOrDefault(g => g.Id == item.GroupId) ?? groupSelect.Items[0];
        }

        private void SendChange(string operation)
        {
            if (working || selectedPeer == null || String.IsNullOrWhiteSpace(code.Text)) { status.Text = "请先选择用户并填写管理口令。"; return; }
            LanTaskInfo selected = tasks.SelectedItem as LanTaskInfo;
            if (operation != "add" && selected == null) { status.Text = "请先选择一条待办。"; return; }
            if (operation != "delete" && String.IsNullOrWhiteSpace(taskText.Text)) { status.Text = "待办内容不能为空。"; return; }
            LanGroupInfo group = groupSelect.SelectedItem as LanGroupInfo;
            LanRequest request = new LanRequest { Operation = operation, AccessCode = code.Text.Trim(), ItemId = selected == null ? null : selected.Id, Text = taskText.Text.Trim(), GroupId = group == null ? null : group.Id, Completed = completed.IsChecked == true };
            working = true; status.Text = "正在同步…"; LanPeer peer = selectedPeer;
            ThreadPool.QueueUserWorkItem(delegate
            {
                LanResponse response = LanService.Send(peer, request);
                window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (disposed) return; working = false;
                    if (!response.Success) { status.Text = response.Error; return; }
                    ApplySnapshot(response.Snapshot); taskText.Clear(); completed.IsChecked = false; status.Text = "修改已实时同步到 " + peer.DisplayName;
                }));
            });
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true; refreshTimer.Stop();
            try { if (window.IsVisible) window.Close(); } catch { }
        }
    }
}
