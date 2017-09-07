﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DamageMeter.Sniffing;
using DamageMeter.UI.Annotations;
using DamageMeter.UI.Windows;
using Tera.Game;
using Microsoft.Win32;
using Tera;
using Tera.Game.Messages;
using Brushes = System.Drawing.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpcodeId = System.UInt16;
using Point = System.Windows.Point;

namespace DamageMeter.UI
{
    /// <summary>
    ///     Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : INotifyPropertyChanged
    {
        private bool _topMost = true;


        private PacketViewModel _packetDetails;
        public PacketViewModel PacketDetails
        {
            get => _packetDetails;
            set
            {
                if (_packetDetails == value) return;
                _packetDetails = value;
                OnPropertyChanged(nameof(PacketDetails));
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            // Handler for exceptions in threads behind forms.
            TeraSniffer.Instance.Enabled = true;
            //TeraSniffer.Instance.Warning += PcapWarning;
            NetworkController.Instance.Connected += HandleConnected;
            NetworkController.Instance.GuildIconAction += InstanceOnGuildIconAction;
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
            Title = "Opcode Searcher V0";
            SystemEvents.SessionEnding += new SessionEndingEventHandler(SystemEvents_SessionEnding);
            NetworkController.Instance.TickUpdated += (msg) => Dispatcher.BeginInvoke(new Action(() => HandleNewMessage(msg)), DispatcherPriority.Background);
            NetworkController.Instance.ResetUi += () => Dispatcher.Invoke(() =>
            {
                All.Clear();
                Known.Clear();
                OpcodeNameConv.Instance.Clear();
            });
            All.CollectionChanged += All_CollectionChanged;
            DataContext = this;
            //((ItemsControl)KnownSw.Content).ItemsSource = Known;

        }

        private int _count;
        private int _queued;
        public int Queued
        {
            get => _queued;
            set
            {
                _queued = value;
                OnPropertyChanged(nameof(Queued));
            }
        }

        private void CheckFindList(ushort opcode, OpcodeEnum opname)
        {
            var opc = OpcodesToFind.FirstOrDefault(x => x.OpcodeName == opname.ToString());
            if (opc == null) { return; }
            if (opc.Opcode == 0)
            {
                opc.Opcode = opcode;
                opc.Confirmed = true;
            }
            else
            {
                if (opc.Opcode == opcode) { opc.Confirmed = true; }
                else
                {
                    opc.Mismatching = opcode;
                }
            }

        }
        private void HandleNewMessage(Tuple<List<ParsedMessage>, Dictionary<OpcodeId, OpcodeEnum>, int> update)
        {
            Queued = update.Item3;
            if (update.Item2.Count != 0)
            {
                foreach (var opcode in update.Item2)
                {
                    Dispatcher.Invoke(() =>
                    {
                        Known.Add(opcode.Key, opcode.Value);
                        CheckFindList(opcode.Key, opcode.Value);
                    });

                    OpcodeNameConv.Instance.Known.Add(opcode.Key, opcode.Value);
                    foreach (var packetViewModel in All.Where(x => x.Message.OpCode == opcode.Key))
                    {
                        packetViewModel.RefreshName();
                    }
                }
                KnownSw.ScrollToBottom();
            }

            foreach (var msg in update.Item1)
            {
                _count++;
                if (msg.Direction == MessageDirection.ServerToClient && ServerCb.IsChecked == false) return;
                if (msg.Direction == MessageDirection.ClientToServer && ClientCb.IsChecked == false) return;
                if (FilteredOpcodes.Count(x => x.Mode == FilterMode.ShowOnly) > 0 && FilteredOpcodes.Where(x => x.Mode == FilterMode.ShowOnly).All(x => x.Opcode != msg.OpCode)) return;
                if (FilteredOpcodes.Any(x => x.Opcode == msg.OpCode && x.Mode == FilterMode.Exclude)) return;
                if (SpamCb.IsChecked == true && All.Count > 0 && All.Last().Message.OpCode == msg.OpCode) return;
                if (_sizeFilter != -1)
                {
                    if (msg.Payload.Count != _sizeFilter) return;
                }
                var vm = new PacketViewModel(msg, _count);
                All.Add(vm);
                if (SearchList.Count > 0)
                {
                    if (SearchList[0].Message.OpCode == msg.OpCode) UpdateSearch(msg.OpCode.ToString(), false); //could be performance intensive
                }
            }
        }

        private void AllSw_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            var b = VisualTreeHelper.GetChild(AllItemsControl, 0) as Border;
            var sw = VisualTreeHelper.GetChild(b, 0) as ScrollViewer;

            if (sw.VerticalOffset == sw.ScrollableHeight)
            {
                _bottom = true;
                NewMessagesBelow = false;
            }
            else _bottom = false;
        }

        private bool _bottom;
        private bool _newMessagesBelow;
        public bool NewMessagesBelow
        {
            get => _newMessagesBelow;
            set
            {
                if (_newMessagesBelow == value) return;
                _newMessagesBelow = value;
                OnPropertyChanged(nameof(NewMessagesBelow));
            }
        }

        private void All_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            var b = VisualTreeHelper.GetChild(AllItemsControl, 0) as Border;
            var sw = VisualTreeHelper.GetChild(b, 0) as ScrollViewer;

            if (_bottom) Dispatcher.Invoke(() => sw.ScrollToBottom());
            else
            {
                NewMessagesBelow = true;
            }
        }
        public ObservableDictionary<ushort, OpcodeEnum> Known { get; set; } = new ObservableDictionary<ushort, OpcodeEnum>();
        public ObservableCollection<PacketViewModel> All { get; set; } = new ObservableCollection<PacketViewModel>();
        public ObservableCollection<OpcodeToFindVm> OpcodesToFind { get; set; } = new ObservableCollection<OpcodeToFindVm>();
        public ObservableCollection<FilteredOpcodeVm> FilteredOpcodes { get; set; } = new ObservableCollection<FilteredOpcodeVm>();
        private void SystemEvents_SessionEnding(object sender, SessionEndingEventArgs e)
        {
            Exit();
        }


        private void InstanceOnGuildIconAction(Bitmap icon)
        {
            void ChangeUi(Bitmap bitmap)
            {
                //Icon = bitmap?.ToImageSource() ?? BasicTeraData.Instance.ImageDatabase.Icon;
                //NotifyIcon.Tray.Icon = bitmap?.GetIcon() ?? BasicTeraData.Instance.ImageDatabase.Tray;
            }

            Dispatcher.Invoke((NetworkController.GuildIconEvent)ChangeUi, icon);
        }

        private void MainWindow_OnClosed(object sender, EventArgs e)
        {
            Exit();
        }

        public void VerifyClose()
        {
            Close();
        }

        public void Exit()
        {
            _topMost = false;
            NetworkController.Instance.Exit();
        }

        public void HandleConnected(string serverName)
        {
            void ChangeTitle(string newServerName)
            {
                Title = newServerName;
            }

            Dispatcher.Invoke((ChangeTitle)ChangeTitle, serverName);
        }

        internal void StayTopMost()
        {
            if (!_topMost || !Topmost)
            {
                Debug.WriteLine("Not topmost");
                return;
            }
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                window.Topmost = false;
                window.Topmost = true;
            }
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            Top = 0;
            Left = 0;
        }


        private void ListEncounter_OnDropDownOpened(object sender, EventArgs e)
        {
            _topMost = false;
        }

        private void Close_MouseLeftButtonDown(object sender, RoutedEventArgs e)
        {
            VerifyClose();
        }

        private delegate void ChangeTitle(string servername);

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void MessageMouseEnter(object sender, MouseEventArgs mouseEventArgs)
        {
            //if (mouseEventArgs.LeftButton == MouseButtonState.Pressed)
            //{
            //    var s = ((Grid)sender);
            //    PacketDetails = s.DataContext as PacketViewModel;
            //    foreach (var packetViewModel in All) { packetViewModel.IsSelected = packetViewModel.Message == PacketDetails.Message; }
            //    OpcodeToWhitelist.Text = PacketDetails.Message.OpCode.ToString();
            //    OpcodeToBlacklist.Text = PacketDetails.Message.OpCode.ToString();
            //}
            var s = sender as Grid;
            var b = s.Children[7] as Button;
            b.Visibility = Visibility.Visible;
        }
        private void MessageClick(object sender, MouseButtonEventArgs e)
        {
            var s = ((Grid)sender);
            PacketDetails = s.DataContext as PacketViewModel;
            foreach (var packetViewModel in All) { packetViewModel.IsSelected = packetViewModel.Message == PacketDetails.Message; }
            OpcodeToFilter.Text = PacketDetails.Message.OpCode.ToString();

        }
        private string FormatData(ArraySegment<byte> selectedPacketPayload)
        {
            var a = selectedPacketPayload.ToArray();
            var s = BitConverter.ToString(a).Replace("-", string.Empty);
            var sb = new StringBuilder();
            var i = 0;
            var count = 0;
            while (true)
            {
                if (s.Length > i + 8)
                {
                    sb.Append(s.Substring(i, 8));
                    sb.Append((count + 1) % 4 == 0 ? "\n" : " ");
                }
                else
                {
                    sb.Append(s.Substring(i));
                    break;
                }
                i += 8;
                count++;
            }
            return sb.ToString();

        }

        private void HexSwChanged(object sender, ScrollChangedEventArgs e)
        {
            var s = sender as ScrollViewer;
            if (s.Name == nameof(HexSw)) TextSw.ScrollToVerticalOffset(HexSw.VerticalOffset);
            else HexSw.ScrollToVerticalOffset(TextSw.VerticalOffset);
        }

        private void ChunkMouseEnter(object sender, MouseEventArgs e)
        {
            var s = sender as Border;
            var dc = (string)s.DataContext;
        }

        private void ClearAll(object sender, RoutedEventArgs e)
        {
            All.Clear();
        }

        private void Dump(object sender, RoutedEventArgs e)
        {
            var lines = new List<string>();
            foreach (KeyValuePair<ushort, OpcodeEnum> keyVal in Known)
            {
                var s = $"{keyVal.Value} = {keyVal.Key}";
                lines.Add(s);
            }
            File.WriteAllLines($"{Environment.CurrentDirectory}/opcodes {DateTime.Now.ToString().Replace('/', '-').Replace(':', '-')}.txt", lines);
        }

        private void CopyPacketDetailsHex(object sender, RoutedEventArgs e)
        {
          
            StringBuilder bldr = new StringBuilder();
            foreach (var a in PacketDetails.Data)
            {
                bldr.Append(a.Hex);
            }
            System.Windows.Clipboard.Clear();
            System.Windows.Clipboard.SetDataObject(bldr.ToString(),false);
          
        }

        private void Load(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog { Filter = "Supported Formats (*.TeraLog)|*.TeraLog" };
            if (openFileDialog.ShowDialog() == false) return;
            NetworkController.Instance.LoadFileName = openFileDialog.FileName;
        }

        private void RemoveFilteredOpcode(object sender, RoutedEventArgs e)
        {
            var s = (System.Windows.Controls.Button)sender;
            FilteredOpcodes.Remove((FilteredOpcodeVm)s.DataContext);
        }

        private void AddBlackListedOpcode(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(OpcodeToFilter.Text)) return;
            if (!ushort.TryParse(OpcodeToFilter.Text, out ushort result)) return;
            if (FilteredOpcodes.FirstOrDefault(x => x.Opcode == result) != null) return;
            FilteredOpcodes.Add(new FilteredOpcodeVm(result, FilterMode.Exclude));
            OpcodeToFilter.Text = "";

        }

        private void AddWhiteListedOpcode(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(OpcodeToFilter.Text)) return;
            if (!ushort.TryParse(OpcodeToFilter.Text, out ushort result)) return;
            if (FilteredOpcodes.FirstOrDefault(x => x.Opcode == result) != null) return;
            FilteredOpcodes.Add(new FilteredOpcodeVm(result, FilterMode.ShowOnly));
            OpcodeToFilter.Text = "";
        }

        private void Save(object sender, RoutedEventArgs e)
        {
            NetworkController.Instance.NeedToSave = true;
        }

        private void UIElement_OnMouseEnter(object sender, MouseEventArgs e)
        {
            var s = sender as FrameworkElement;
            var bvm = s.DataContext as ByteViewModel;
            bvm.IsHovered = true;
            int i = 0;
            i = HexIc.Items.IndexOf(bvm);
            if (i == -1) i = TextIc.Items.IndexOf(bvm);
            PacketDetails.RefreshData(i);
        }

        private void UIElement_OnMouseLeave(object sender, MouseEventArgs e)
        {
            var s = sender as FrameworkElement;
            var bvm = s.DataContext as ByteViewModel;
            bvm.IsHovered = false;

        }

        private void UIElement_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var b = VisualTreeHelper.GetChild(AllItemsControl, 0) as Border;
            var sw = VisualTreeHelper.GetChild(b, 0) as ScrollViewer;
            sw.ScrollToBottom();
        }

        private void LoadOpcode(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog { Filter = "Supported Formats, with / without '=' separator (*.txt)|*.txt" };
            if (openFileDialog.ShowDialog() == false) return;
            NetworkController.Instance.StrictCheck = false;
            NetworkController.Instance.LoadOpcodeCheck = openFileDialog.FileName;
        }

        private int _sizeFilter = -1;
        private void TextBoxBase_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            var s = sender as System.Windows.Controls.TextBox;
            if (string.IsNullOrEmpty(s.Text))
            {
                _sizeFilter = -1;
                return;
            }
            try
            {
                _sizeFilter = Convert.ToInt32(s.Text);
            }
            catch (Exception exception)
            {
                _sizeFilter = -1;
                return;
            }
        }

        public List<PacketViewModel> SearchList { get; set; } = new List<PacketViewModel>();
        private void SearchBoxChanged(object sender, TextChangedEventArgs e)
        {
            var s = sender as System.Windows.Controls.TextBox;
            UpdateSearch(s.Text, true);
        }

        private void UpdateSearch(string q, bool bringIntoView)
        {
            SearchList.Clear();
            OnPropertyChanged(nameof(SearchList));
            foreach (var packetViewModel in All)
            {
                //packetViewModel.IsSearched = true;
                packetViewModel.IsSearched = false;
            }
            if (string.IsNullOrEmpty(q))
            {
                foreach (var packetViewModel in All)
                {
                    //packetViewModel.IsSearched = true;
                    packetViewModel.IsSearched = false;
                }

                return;
            }
            try
            {
                var query = Convert.ToUInt16(q);
                //search by opcode
                foreach (var packetViewModel in All.Where(x => x.Message.OpCode == query))
                {
                    packetViewModel.IsSearched = true;
                    SearchList.Add(packetViewModel);
                }
                if (SearchList.Count != 0)
                {
                    var i = All.IndexOf(SearchList[0]);
                    if (bringIntoView)
                    {
                        //var container = AllItemsControl.ItemContainerGenerator.ContainerFromItem(All[i]) as FrameworkElement;
                        //container.BringIntoView();
                        AllItemsControl.VirtualizedScrollIntoView(All[i]);
                        PacketDetails = All[i];
                    }
                    foreach (var packetViewModel in All) { packetViewModel.IsSelected = packetViewModel == All[i]; }

                }
            }
            catch (Exception exception)
            {
                //search by opcodename

                OpcodeEnum opEnum;
                try
                {
                    opEnum = (OpcodeEnum)Enum.Parse(typeof(OpcodeEnum), q);
                }
                catch (Exception e1) { return; }


                foreach (var packetViewModel in All.Where(x => x.Message.OpCode == OpcodeFinder.Instance.GetOpcode(opEnum)))
                {
                    packetViewModel.IsSearched = true;
                    SearchList.Add(packetViewModel);
                }
                if (SearchList.Count != 0)
                {
                    var i = All.IndexOf(SearchList[0]);
                    if (bringIntoView)
                    {

                        //var container = AllItemsControl.ItemContainerGenerator.ContainerFromItem(All[i]) as FrameworkElement;
                        //container.BringIntoView();
                        AllItemsControl.VirtualizedScrollIntoView(All[i]);

                        PacketDetails = All[i];
                    }
                    foreach (var packetViewModel in All) { packetViewModel.IsSelected = packetViewModel == All[i]; }

                }
            }
            OnPropertyChanged(nameof(SearchList));

        }

        private int _currentSelectedItemIndex = 0;
        private void PreviousResult(object sender, RoutedEventArgs e)
        {
            if (SearchList.Count <2) return;
            if (_currentSelectedItemIndex == 0) _currentSelectedItemIndex = SearchList.Count - 1;
            else _currentSelectedItemIndex--;
            var i = All.IndexOf(SearchList[_currentSelectedItemIndex]);
            //var container = AllItemsControl.ItemContainerGenerator.ContainerFromItem(All[i]) as FrameworkElement;
            //container.BringIntoView();
            AllItemsControl.VirtualizedScrollIntoView(All[i]);

            PacketDetails = All[i];
            foreach (var packetViewModel in All) { packetViewModel.IsSelected = packetViewModel == All[i]; }

        }
        private void NextResult(object sender, RoutedEventArgs e)
        {
            if (SearchList.Count <2) return;
            if (_currentSelectedItemIndex == SearchList.Count - 1) _currentSelectedItemIndex = 0;
            else _currentSelectedItemIndex++;
            var i = All.IndexOf(SearchList[_currentSelectedItemIndex]);
            //var container = AllItemsControl.ItemContainerGenerator.ContainerFromItem(All[i]) as FrameworkElement;
            //container.BringIntoView();
            AllItemsControl.VirtualizedScrollIntoView(All[i]);

            PacketDetails = All[i];
            foreach (var packetViewModel in All) { packetViewModel.IsSelected = packetViewModel == All[i]; }

        }

        private void LoadOpcodeStrict(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog { Filter = "Supported Formats, with / without '=' separator (*.txt)|*.txt" };
            if (openFileDialog.ShowDialog() == false) return;
            NetworkController.Instance.StrictCheck = true;
            NetworkController.Instance.LoadOpcodeCheck = openFileDialog.FileName;
        }

        private string _currentFile;
        private void LoadList(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog { Filter = "Opcode names list (*.txt, *map)|*.txt;*.map" };
            if (openFileDialog.ShowDialog() == false) return;
            _currentFile = openFileDialog.FileName;
            var f = File.OpenText(_currentFile);
            OpcodesToFind.Clear();
            while (true)
            {
                uint opc = 0;
                var l = f.ReadLine();
                if (string.IsNullOrEmpty(l)) break;
                string opn = String.Empty;
                if (l.Contains("#"))
                {
                    var symbolIndex = l.IndexOf("#");
                    if (symbolIndex == 0) continue;
                    else
                    {
                        opn = l.Substring(0, symbolIndex - 1);
                    }
                }
                else
                {
                    opn = l;
                }

                var split = opn.Split(new string[] { " = " }, StringSplitOptions.RemoveEmptyEntries);
                if (split.Length > 1)
                {
                    opn = split[0];
                    opc = Convert.ToUInt32(split[1]);
                    OpcodesToFind.Add(new OpcodeToFindVm(opn, opc));
                    continue;
                }
                split = l.Split(' ');
                if (split.Length == 2)
                {
                    opn = split[0];
                    opc = Convert.ToUInt32(split[1]);
                    OpcodesToFind.Add(new OpcodeToFindVm(opn, opc));
                    continue;
                }
                OpcodesToFind.Add(new OpcodeToFindVm(opn, opc));

            }
            foreach (KeyValuePair<ushort, OpcodeEnum> o in Known)
            {
                CheckFindList(o.Key, o.Value);
            }

        }

        private void SaveList(object sender, RoutedEventArgs e)
        {
            var lines = new List<string>();
            foreach (var opcodeToFindVm in OpcodesToFind)
            {
                var line = $"{opcodeToFindVm.OpcodeName} = {opcodeToFindVm.Opcode}";
                lines.Add(line);
            }
            File.WriteAllLines(_currentFile, lines);
        }

        private void MessageMouseLeave(object sender, MouseEventArgs e)
        {
            var s = sender as Grid;
            var b = s.Children[7] as Button;
            b.Visibility = Visibility.Collapsed;
        }

        private void AllSwScrolled(object sender, MouseWheelEventArgs e)
        {
            ScrollViewer s = (ScrollViewer)sender;
            var offset = e.Delta > 0 ? -2 : 2;
            s.ScrollToVerticalOffset(s.VerticalOffset - offset);
            e.Handled = true;
            if (s.VerticalOffset == 0)
            {
                _bottom = true;
            }
            else
            {

                _bottom = false;
            }
        }

        private void TabClicked(object sender, MouseButtonEventArgs e)
        {
            var s = sender as FrameworkElement;
            var w = s.ActualWidth;
            var tp = s.TemplatedParent as FrameworkElement;
            var p = tp.Parent as UIElement;
            var r = s.TranslatePoint(new Point(0, 0), p);
            var sizeAn = new DoubleAnimation(w, TimeSpan.FromMilliseconds(250)) { EasingFunction = new QuadraticEase() };
            var posAn = new DoubleAnimation(r.X, TimeSpan.FromMilliseconds(250)) { EasingFunction = new QuadraticEase() };

            TabSelectionRect.BeginAnimation(WidthProperty, sizeAn);
            TabSelectionRect.RenderTransform.BeginAnimation(TranslateTransform.XProperty, posAn);
        }

        private void ClearAllFilters(object sender, RoutedEventArgs e)
        {
            FilteredOpcodes.Clear();
        }

        private void HideLeftSlide(object sender, MouseButtonEventArgs e)
        {
            LeftSlide.RenderTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-230,TimeSpan.FromMilliseconds(150)) {EasingFunction = new QuadraticEase()});
        }

        private void OpenLeftSlide(object sender, RoutedEventArgs e)
        {
            LeftSlide.RenderTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuadraticEase() });
        }
    }
    public class ModeToColor : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var m = (FilterMode) value;
            switch (m)
            {
                case FilterMode.Exclude: return new SolidColorBrush(Colors.Crimson);
                case FilterMode.ShowOnly: return  new SolidColorBrush(Colors.MediumSeaGreen);
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class ModeToString : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var m = (FilterMode)value;
            switch (m)
            {
                case FilterMode.Exclude: return "HIDE";
                case FilterMode.ShowOnly: return "SHOW";
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class FilteredOpcodeVm 
    {
        public ushort Opcode { get; }
        public FilterMode Mode { get; }

        public FilteredOpcodeVm(ushort opc, FilterMode f)
        {
            Opcode = opc;
            Mode = f;
        }
    }
    public static class ItemsControlExtensions
    {
        public static void VirtualizedScrollIntoView(this ItemsControl control, object item)
        {
            try
            {
                // this is basically getting a reference to the ScrollViewer defined in the ItemsControl's style (identified above).
                // you *could* enumerate over the ItemsControl's children until you hit a scroll viewer, but this is quick and
                // dirty!
                // First 0 in the GetChild returns the Border from the ControlTemplate, and the second 0 gets the ScrollViewer from
                // the Border.
                ScrollViewer sv = VisualTreeHelper.GetChild(VisualTreeHelper.GetChild((DependencyObject)control, 0), 0) as ScrollViewer;
                // now get the index of the item your passing in
                int index = control.Items.IndexOf(item);
                if (index != -1)
                {
                    // since the scroll viewer is using content scrolling not pixel based scrolling we just tell it to scroll to the index of the item
                    // and viola!  we scroll there!
                    sv.ScrollToVerticalOffset(index);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("What the..." + ex.Message);
            }
        }
    }

    public class DirectionToColor : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var v = (MessageDirection)value;
            var c = Colors.Gray;
            if (v == MessageDirection.ServerToClient) c = Color.FromArgb(0xcc, Colors.DodgerBlue.R, Colors.DodgerBlue.G, Colors.DodgerBlue.B);
            if (v == MessageDirection.ClientToServer) c = Color.FromArgb(0xcc, Colors.DarkOrange.R, Colors.DarkOrange.G, Colors.DarkOrange.B);
            return new SolidColorBrush(c);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class OpcodeNameConv : IValueConverter
    {
        private static OpcodeNameConv _instance;
        public static OpcodeNameConv Instance => _instance ?? (_instance = new OpcodeNameConv());
        public ObservableDictionary<ushort, OpcodeEnum> Known { get; set; } = new ObservableDictionary<ushort, OpcodeEnum>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                //it's hex string
                var opn = (string) value;
                if (opn.Length == 4 && opn[1] != '_')
                {
                    if (Instance.Known.TryGetValue(System.Convert.ToUInt16(opn, 16), out OpcodeEnum opc)) { return opc.ToString(); }
                }
            }
            catch
            {
                //it's ushort
                var opn = (ushort) value; 
                if (Instance.Known.TryGetValue(opn, out OpcodeEnum opc)) { return opc.ToString(); }
            }
            return "-";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            Known.Clear();
        }
    }
    public class HexPayloadConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var arr = (ArraySegment<byte>)value;
            var a = arr.ToArray();
            return BitConverter.ToString(a).Replace("-", string.Empty);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class TextPayloadConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var arr = (ArraySegment<byte>)value;
            var a = arr.ToArray();
            var sb = new StringBuilder();
            for (int i = 0; i < a.Length; i++)
            {
                var c = (char)a[i];
                if (c > 0x1f && c < 0x80) sb.Append(c);
                else sb.Append("⋅");
                if ((i + 1) % 16 == 0) sb.Append("\n");
            }
            return sb.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class BoolToVisibleHidden : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var v = (bool)value;
            return v ? Visibility.Visible : Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ListNotEmptyToBool : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var v = (int)value;
            return (v > 0) ? Visibility.Visible : Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PacketViewModel : INotifyPropertyChanged
    {
        public ParsedMessage Message { get; }
        public int Count { get; }
        private bool _isSelected = false;
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public string Time => $"{Message.Time.ToString("HH:mm:ss.fff")}";
        public List<List<string>> RowsHex => ParseDataHex(Message.Payload);
        public List<List<string>> RowsText => ParseDataText(Message.Payload);

        private List<List<string>> ParseDataHex(ArraySegment<byte> p)
        {
            var a = p.ToArray();
            var s = BitConverter.ToString(a).Replace("-", string.Empty);
            var rows = (s.Length / 32) + 1;
            var result = new List<List<string>>();
            for (int i = 0; i < rows; i++)
            {
                var row = new List<string>();
                for (int j = 0; j < 32; j += 8)
                {
                    if (j + 32 * i >= s.Length) continue;

                    var chunk = s.Substring(j + (32 * i)).Length <= 8 ? s.Substring(j + (32 * i)) : s.Substring(j + (32 * i), 8);
                    row.Add(chunk);
                }
                for (int j = row.Count; j < 4; j++)
                {
                    row.Add("");
                }
                result.Add(row);
            }
            return result;
        }
        private List<List<string>> ParseDataText(ArraySegment<byte> p)
        {
            var a = p.ToArray();
            var sb = new StringBuilder();
            for (int i = 0; i < a.Length; i++)
            {
                var c = (char)a[i];
                if (c > 0x21 && c < 0x80) sb.Append(c);
                else sb.Append("⋅");
                //if ((i + 1) % 16 == 0) sb.Append("\n");
            }
            var s = sb.ToString();

            var rows = (s.Length / 16) + 1;
            var result = new List<List<string>>();
            for (int i = 0; i < rows; i++)
            {
                var row = new List<string>();
                for (int j = 0; j < 16; j += 4)
                {
                    if (j + 16 * i >= s.Length) continue;

                    var chunk = s.Substring(j + (16 * i)).Length <= 4 ? s.Substring(j + (16 * i)) : s.Substring(j + (16 * i), 4);
                    row.Add(chunk);
                }
                for (int j = row.Count; j < 4; j++)
                {
                    row.Add("");
                }
                result.Add(row);
            }
            return result;
        }

        private List<ByteViewModel> _data;
        private bool _isSearched;
        public List<ByteViewModel> Data => _data ?? (_data = BuildByteView());

        public bool IsSearched
        {
            get => _isSearched;
            set
            {
                //if (_isSelected == value) return;
                _isSearched = value;
                OnPropertyChanged(nameof(IsSearched));
            }
        }

        private List<ByteViewModel> BuildByteView()
        {
            var res = new List<ByteViewModel>();
            for (int i = 0; i < Message.Payload.Count; i += 4)
            {
                var count = i + 4 > Message.Payload.Count ? Message.Payload.Count - i : 4;
                var chunk = new ArraySegment<byte>(Message.Payload.ToArray(), i, count);
                var bvm = new ByteViewModel(chunk.ToArray());
                res.Add(bvm);
            }
            return res;
        }

        public PacketViewModel(ParsedMessage message, int c)
        {
            Message = message;
            Count = c;
        }


        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void RefreshName()
        {
            OnPropertyChanged(nameof(Message));
        }

        public void RefreshData(int i)
        {
            Data[i].Refresh();

        }
    }

    public class ByteViewModel : INotifyPropertyChanged
    {
        private byte[] _value;
        public string Hex => BitConverter.ToString(_value).Replace("-", string.Empty);
        public string Text => BuildString();

        private string BuildString()
        {
            var sb = new StringBuilder();
            foreach (var b in _value)
            {
                sb.Append(b > 0x21 && b < 0x80 ? (char)b : '⋅');
            }
            return sb.ToString();
        }
        private bool _isHovered;
        public bool IsHovered
        {
            get => _isHovered;
            set
            {
                if (_isHovered == value) return;
                _isHovered = value;
                OnPropertyChanged((nameof(IsHovered)));
            }
        }

        public ByteViewModel(byte[] v)
        {
            _value = v;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(IsHovered));
        }
    }

    public enum FilterMode
    {
        Exclude = 0,
        ShowOnly = 1
    }
}