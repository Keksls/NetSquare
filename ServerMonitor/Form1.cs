using NetSquare.Core;
using NetSquare.Server;
using NetSquare.Server.Server;
using NetSquare.Server.Worlds;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

#region Source
namespace ServerMonitor
{
    /// <summary>
    /// Represents the form1 component.
    /// </summary>
    public class Form1 : Window
    {
        /// <summary>
        /// Stores the receptions speed values value.
        /// </summary>
        private readonly List<int> receptionsSpeedValues = new List<int>();
        /// <summary>
        /// Stores the sending speed values value.
        /// </summary>
        private readonly List<int> sendingSpeedValues = new List<int>();
        /// <summary>
        /// Stores the receptions size values value.
        /// </summary>
        private readonly List<float> receptionsSizeValues = new List<float>();
        /// <summary>
        /// Stores the sending size values value.
        /// </summary>
        private readonly List<float> sendingSizeValues = new List<float>();
        /// <summary>
        /// Stores the history lock value.
        /// </summary>
        private readonly object historyLock = new object();
        /// <summary>
        /// Stores the metric values value.
        /// </summary>
        private readonly Dictionary<string, TextBlock> metricValues = new Dictionary<string, TextBlock>();
        /// <summary>
        /// Stores the world title value.
        /// </summary>
        private readonly TextBlock worldTitle;
        /// <summary>
        /// Stores the world details value.
        /// </summary>
        private readonly TextBlock worldDetails;
        /// <summary>
        /// Stores the world health value.
        /// </summary>
        private readonly TextBlock worldHealth;
        /// <summary>
        /// Stores the world tree value.
        /// </summary>
        private readonly TreeView worldTree;
        /// <summary>
        /// Stores the generated config path text value.
        /// </summary>
        private readonly TextBlock generatedConfigPathText;
        /// <summary>
        /// Stores the current world displayed by the monitor.
        /// </summary>
        private NetSquareWorld currentWorld;
        /// <summary>
        /// Stores the active trace recorder.
        /// </summary>
        private readonly NetSquareTraceRecorder traceRecorder = new NetSquareTraceRecorder();
        /// <summary>
        /// Stores the world currently traced.
        /// </summary>
        private NetSquareWorld tracedWorld;
        /// <summary>
        /// Stores the log list value.
        /// </summary>
        private readonly ListBox logList;
        /// <summary>
        /// Stores the message chart value.
        /// </summary>
        private readonly MetricChart messageChart;
        /// <summary>
        /// Stores the bandwidth chart value.
        /// </summary>
        private readonly MetricChart bandwidthChart;
        /// <summary>
        /// Stores the max length value.
        /// </summary>
        private int maxLength = 600;
        /// <summary>
        /// Stores the each time invoke value.
        /// </summary>
        private int eachTimeInvoke = 1;
        /// <summary>
        /// Stores the invoke index value.
        /// </summary>
        private int invokeIndex = 0;
        /// <summary>
        /// Stores the is closed value.
        /// </summary>
        private bool isClosed;

        /// <summary>
        /// Gets or sets the is disposed value.
        /// </summary>
        public bool IsDisposed { get { return isClosed; } }

        /// <summary>
        /// Initializes a new instance of the form1 class.
        /// </summary>
        public Form1()
        {
            Title = "NetSquare Server Monitor";
            Width = 1280;
            Height = 820;
            MinWidth = 980;
            MinHeight = 620;
            Background = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");
            Closed += (sender, args) => { isClosed = true; };

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });
            root.Margin = new Thickness(16);
            Content = root;

            TextBlock title = new TextBlock
            {
                Text = "NetSquare Server Monitor",
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#172033"),
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(title);

            UniformGrid metricsGrid = new UniformGrid
            {
                Columns = 4,
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid.SetRow(metricsGrid, 1);
            root.Children.Add(metricsGrid);

            AddMetric(metricsGrid, "clients", "Clients", "0", "#1F7A8C");
            AddMetric(metricsGrid, "listeners", "Listeners", "0", "#6D597A");
            AddMetric(metricsGrid, "queued", "Queue", "0", "#9C6644");
            AddMetric(metricsGrid, "tosend", "To send", "0", "#2F4858");
            AddMetric(metricsGrid, "down", "Down", "0.00 ko/s", "#246A73");
            AddMetric(metricsGrid, "up", "Up", "0.00 ko/s", "#B56576");
            AddMetric(metricsGrid, "rxmsg", "Rx msg/s", "0", "#287271");
            AddMetric(metricsGrid, "txmsg", "Tx msg/s", "0", "#8F5D46");
            AddMetric(metricsGrid, "drops", "Dropped", "0", "#A23E48");

            Grid main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(main, 2);
            root.Children.Add(main);

            messageChart = new MetricChart
            {
                Title = "Messages / second",
                SeriesAName = "received",
                SeriesBName = "sent",
                SeriesABrush = BrushFromHex("#1F7A8C"),
                SeriesBBrush = BrushFromHex("#B56576"),
                Margin = new Thickness(0, 0, 12, 12)
            };
            main.Children.Add(messageChart);

            bandwidthChart = new MetricChart
            {
                Title = "Bandwidth ko/s",
                SeriesAName = "down",
                SeriesBName = "up",
                SeriesABrush = BrushFromHex("#2A9D8F"),
                SeriesBBrush = BrushFromHex("#E76F51"),
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetRow(bandwidthChart, 1);
            main.Children.Add(bandwidthChart);

            Border worldPanel = CreatePanel();
            Grid.SetColumn(worldPanel, 1);
            Grid.SetRowSpan(worldPanel, 2);
            main.Children.Add(worldPanel);

            StackPanel worldStack = new StackPanel();
            worldPanel.Child = worldStack;
            worldTitle = new TextBlock
            {
                Text = "World",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#172033"),
                Margin = new Thickness(0, 0, 0, 12)
            };
            worldStack.Children.Add(worldTitle);

            worldHealth = new TextBlock
            {
                Text = "No world data",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#8F5D46"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            worldStack.Children.Add(worldHealth);

            worldDetails = new TextBlock
            {
                Text = "Waiting for updates.",
                FontSize = 13,
                Foreground = BrushFromHex("#4A5568"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22
            };
            worldStack.Children.Add(worldDetails);

            Button generateConfigButton = new Button
            {
                Content = "Generate server config JSON",
                Margin = new Thickness(0, 14, 0, 8),
                Padding = new Thickness(10, 6, 10, 6),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            generateConfigButton.Click += GenerateConfigButton_Click;
            worldStack.Children.Add(generateConfigButton);

            generatedConfigPathText = new TextBlock
            {
                Text = string.Empty,
                FontSize = 12,
                Foreground = BrushFromHex("#718096"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            worldStack.Children.Add(generatedConfigPathText);

            StackPanel traceButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)
            };
            worldStack.Children.Add(traceButtons);

            Button startTraceButton = new Button
            {
                Content = "Start trace",
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 0)
            };
            startTraceButton.Click += StartTraceButton_Click;
            traceButtons.Children.Add(startTraceButton);

            Button stopTraceButton = new Button
            {
                Content = "Stop / export trace",
                Padding = new Thickness(10, 6, 10, 6)
            };
            stopTraceButton.Click += StopTraceButton_Click;
            traceButtons.Children.Add(stopTraceButton);

            worldTree = new TreeView
            {
                BorderBrush = BrushFromHex("#D8DEE9"),
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                MinHeight = 260
            };
            worldStack.Children.Add(worldTree);

            Border logPanel = CreatePanel();
            logPanel.Margin = new Thickness(0, 14, 0, 0);
            Grid.SetRow(logPanel, 3);
            root.Children.Add(logPanel);

            DockPanel logDock = new DockPanel();
            logPanel.Child = logDock;
            TextBlock logTitle = new TextBlock
            {
                Text = "Live log",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#172033"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(logTitle, Dock.Top);
            logDock.Children.Add(logTitle);

            logList = new ListBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = BrushFromHex("#2D3748"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            };
            logDock.Children.Add(logList);
        }

        /// <summary>
        /// Executes the initialize operation.
        /// </summary>
        public void Initialize(int maxlenght, int eachtimeinvoke)
        {
            maxLength = Math.Max(30, maxlenght);
            eachTimeInvoke = Math.Max(1, eachtimeinvoke);
        }

        /// <summary>
        /// Executes the write operation.
        /// </summary>
        public void Write(string text)
        {
            RunOnUi(() =>
            {
                logList.Items.Add(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + text);
                while (logList.Items.Count > 200)
                    logList.Items.RemoveAt(0);
                if (logList.Items.Count > 0)
                    logList.ScrollIntoView(logList.Items[logList.Items.Count - 1]);
            });
        }

        /// <summary>
        /// Executes the clear operation.
        /// </summary>
        public void Clear()
        {
            RunOnUi(() => logList.Items.Clear());
        }

        /// <summary>
        /// Executes the update statistics operation.
        /// </summary>
        public void UpdateStatistics(ServerStatistics statistics)
        {
            lock (historyLock)
            {
                receptionsSpeedValues.Add(statistics.NbMessagesReceiving);
                sendingSpeedValues.Add(statistics.NbMessagesSending);
                Trim(receptionsSpeedValues);
                Trim(sendingSpeedValues);

                receptionsSizeValues.Add(statistics.Downloading);
                sendingSizeValues.Add(statistics.Uploading);
                Trim(receptionsSizeValues);
                Trim(sendingSizeValues);
            }

            if (invokeIndex < eachTimeInvoke)
            {
                invokeIndex++;
                return;
            }
            invokeIndex = 0;

            RunOnUi(() =>
            {
                SetMetric("clients", statistics.NbClientsConnected.ToString(CultureInfo.InvariantCulture));
                SetMetric("listeners", statistics.NbListeners.ToString(CultureInfo.InvariantCulture));
                SetMetric("queued", statistics.NbProcessingMessages.ToString(CultureInfo.InvariantCulture));
                SetMetric("tosend", statistics.NbMessagesToSend.ToString(CultureInfo.InvariantCulture));
                SetMetric("down", statistics.Downloading.ToString("0.00", CultureInfo.InvariantCulture) + " ko/s");
                SetMetric("up", statistics.Uploading.ToString("0.00", CultureInfo.InvariantCulture) + " ko/s");
                SetMetric("rxmsg", statistics.NbMessagesReceiving.ToString(CultureInfo.InvariantCulture));
                SetMetric("txmsg", statistics.NbMessagesSending.ToString(CultureInfo.InvariantCulture));
                SetMetric("drops", statistics.NbMessagesDropped.ToString(CultureInfo.InvariantCulture));

                lock (historyLock)
                {
                    messageChart.SetValues(receptionsSpeedValues.Select(v => (double)v), sendingSpeedValues.Select(v => (double)v));
                    bandwidthChart.SetValues(receptionsSizeValues.Select(v => (double)v), sendingSizeValues.Select(v => (double)v));
                }
            });
        }

        /// <summary>
        /// Executes the update world data operation.
        /// </summary>
        public void UpdateWorldData(NetSquareWorld world)
        {
            if (invokeIndex < eachTimeInvoke)
                return;

            RunOnUi(() =>
            {
                if (world == null)
                {
                    currentWorld = null;
                    worldTitle.Text = "World";
                    worldHealth.Text = "No world data";
                    worldHealth.Foreground = BrushFromHex("#8F5D46");
                    worldDetails.Text = "Waiting for updates.";
                    worldTree.Items.Clear();
                    return;
                }

                currentWorld = world;
                NetSquareWorldSnapshot snapshot = world.CreateSnapshot();
                worldTitle.Text = "World " + snapshot.ID + " - " + snapshot.Name;
                worldHealth.Text = GetWorldHealth(snapshot);
                worldHealth.Foreground = snapshot.UseSpatializer ? BrushFromHex("#287271") : BrushFromHex("#9C6644");
                worldDetails.Text =
                    "Clients: " + snapshot.ClientCount + " / " + snapshot.MaxClientsInWorld + Environment.NewLine +
                    "Synchronizer: " + (snapshot.UseSynchronizer ? "enabled" : "disabled") + Environment.NewLine +
                    "Spatializer: " + (snapshot.Spatializer != null ? snapshot.Spatializer.Type : "none") + Environment.NewLine +
                    "Spatial sync: " + (snapshot.Spatializer != null ? snapshot.Spatializer.SynchFrequency + " ms" : "-") + Environment.NewLine +
                    "Spatialization: " + (snapshot.Spatializer != null ? snapshot.Spatializer.SpatializationFrequency + " ms" : "-") + Environment.NewLine +
                    "Static entities: " + (snapshot.Spatializer != null ? snapshot.Spatializer.StaticEntitiesCount.ToString(CultureInfo.InvariantCulture) : "-") + Environment.NewLine +
                    "Pending frames: " + (snapshot.Spatializer != null ? snapshot.Spatializer.PendingFrameCount.ToString(CultureInfo.InvariantCulture) : "0");
                UpdateWorldTree(snapshot);
            });
        }

        /// <summary>
        /// Executes the get world health operation.
        /// </summary>
        private string GetWorldHealth(NetSquareWorldSnapshot world)
        {
            if (!world.UseSpatializer || world.Spatializer == null)
                return "Spatializer disabled";
            if (world.Spatializer.SynchFrequency > 1000)
                return "Sync slowed down";
            return "Spatializer active";
        }

        /// <summary>
        /// Executes the generate config button click operation.
        /// </summary>
        private void GenerateConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(Environment.CurrentDirectory, "netsquare.generated.config.json");
                NetSquareServerConfigGenerator.WriteDefault(path);
                generatedConfigPathText.Text = path;
                Write("Generated server config JSON: " + path);
            }
            catch (Exception ex)
            {
                generatedConfigPathText.Text = ex.Message;
                Write("Failed to generate server config JSON: " + ex.Message);
            }
        }

        /// <summary>
        /// Executes the start trace button click operation.
        /// </summary>
        private void StartTraceButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentWorld == null || currentWorld.Spatializer == null)
            {
                Write("Cannot start trace: no spatialized world is selected.");
                return;
            }

            tracedWorld = currentWorld;
            traceRecorder.MaxEntries = 12000;
            traceRecorder.Start();
            tracedWorld.Spatializer.TraceRecorder = traceRecorder;
            Write("Trace recording started for world " + tracedWorld.ID);
        }

        /// <summary>
        /// Executes the stop trace button click operation.
        /// </summary>
        private void StopTraceButton_Click(object sender, RoutedEventArgs e)
        {
            if (!traceRecorder.IsRecording)
            {
                Write("No trace recording is active.");
                return;
            }

            traceRecorder.Stop();
            if (tracedWorld != null && tracedWorld.Spatializer != null && tracedWorld.Spatializer.TraceRecorder == traceRecorder)
                tracedWorld.Spatializer.TraceRecorder = null;

            string path = Path.Combine(Environment.CurrentDirectory, "netsquare-trace-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
            File.WriteAllText(path, traceRecorder.ToJson());
            Write("Trace exported: " + path);
        }

        /// <summary>
        /// Updates the world inspector tree.
        /// </summary>
        /// <param name="snapshot">World snapshot to display.</param>
        private void UpdateWorldTree(NetSquareWorldSnapshot snapshot)
        {
            worldTree.Items.Clear();
            if (snapshot == null)
                return;

            TreeViewItem root = CreateTreeItem("World " + snapshot.ID + " (" + snapshot.ClientCount + " clients)", true);
            worldTree.Items.Add(root);

            if (snapshot.Spatializer != null)
            {
                NetSquareSpatializerSnapshot spatializer = snapshot.Spatializer;
                TreeViewItem spatializerItem = CreateTreeItem("Spatializer: " + spatializer.Type, true);
                spatializerItem.Items.Add(CreateTreeItem("Sync " + spatializer.SynchFrequency + " ms / spatialization " + spatializer.SpatializationFrequency + " ms", false));
                spatializerItem.Items.Add(CreateTreeItem("Pending frames " + spatializer.PendingFrameCount + " / static entities " + spatializer.StaticEntitiesCount, false));
                if (spatializer.ChunkSize > 0)
                    spatializerItem.Items.Add(CreateTreeItem("Chunks " + spatializer.ChunkWidth + "x" + spatializer.ChunkHeight + " size " + spatializer.ChunkSize.ToString("0.##", CultureInfo.InvariantCulture), false));
                if (spatializer.MaxViewDistance > 0)
                    spatializerItem.Items.Add(CreateTreeItem("View distance " + spatializer.MaxViewDistance.ToString("0.##", CultureInfo.InvariantCulture), false));
                root.Items.Add(spatializerItem);

                AddChunkNodes(spatializerItem, spatializer);
            }

            TreeViewItem clientsItem = CreateTreeItem("Clients", true);
            root.Items.Add(clientsItem);
            for (int i = 0; i < snapshot.Clients.Count; i++)
            {
                NetSquareWorldClientSnapshot client = snapshot.Clients[i];
                string position = client.X.ToString("0.##", CultureInfo.InvariantCulture) + ", " + client.Y.ToString("0.##", CultureInfo.InvariantCulture) + ", " + client.Z.ToString("0.##", CultureInfo.InvariantCulture);
                TreeViewItem clientItem = CreateTreeItem("Client " + client.ClientID + "  pos " + position, false);
                clientItem.Items.Add(CreateTreeItem("Visible: " + FormatClientList(client.VisibleClientIDs), false));
                clientItem.Items.Add(CreateTreeItem("Pending frames: " + client.PendingFrameCount, false));
                clientsItem.Items.Add(clientItem);
            }
        }

        /// <summary>
        /// Adds chunk nodes to the spatializer tree item.
        /// </summary>
        /// <param name="parent">Parent tree item.</param>
        /// <param name="spatializer">Spatializer snapshot.</param>
        private static void AddChunkNodes(TreeViewItem parent, NetSquareSpatializerSnapshot spatializer)
        {
            if (spatializer.Chunks == null || spatializer.Chunks.Count == 0)
                return;

            TreeViewItem chunksItem = CreateTreeItem("Non-empty chunks", false);
            int displayed = 0;
            for (int i = 0; i < spatializer.Chunks.Count; i++)
            {
                NetSquareSpatialChunkSnapshot chunk = spatializer.Chunks[i];
                if (chunk.ClientCount == 0 && chunk.StaticEntityCount == 0)
                    continue;

                chunksItem.Items.Add(CreateTreeItem("[" + chunk.X + "," + chunk.Y + "] clients " + chunk.ClientCount + " static " + chunk.StaticEntityCount, false));
                displayed++;
                if (displayed >= 96)
                {
                    chunksItem.Items.Add(CreateTreeItem("... truncated", false));
                    break;
                }
            }

            if (displayed > 0)
                parent.Items.Add(chunksItem);
        }

        /// <summary>
        /// Formats client ids for display.
        /// </summary>
        /// <param name="clientIDs">Client ids to format.</param>
        /// <returns>Formatted client id list.</returns>
        private static string FormatClientList(List<uint> clientIDs)
        {
            if (clientIDs == null || clientIDs.Count == 0)
                return "-";

            return string.Join(", ", clientIDs.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray());
        }

        /// <summary>
        /// Creates a tree item.
        /// </summary>
        /// <param name="header">Item header.</param>
        /// <param name="isExpanded">Whether the item is expanded.</param>
        /// <returns>Tree item.</returns>
        private static TreeViewItem CreateTreeItem(string header, bool isExpanded)
        {
            return new TreeViewItem
            {
                Header = header,
                IsExpanded = isExpanded
            };
        }

        /// <summary>
        /// Executes the add metric operation.
        /// </summary>
        private void AddMetric(Panel parent, string key, string label, string value, string accent)
        {
            Border card = CreatePanel();
            card.Margin = new Thickness(0, 0, 10, 10);
            StackPanel stack = new StackPanel();
            card.Child = stack;

            TextBlock labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = BrushFromHex("#718096"),
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(labelBlock);

            TextBlock valueBlock = new TextBlock
            {
                Text = value,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex(accent)
            };
            stack.Children.Add(valueBlock);
            metricValues[key] = valueBlock;
            parent.Children.Add(card);
        }

        /// <summary>
        /// Executes the set metric operation.
        /// </summary>
        private void SetMetric(string key, string value)
        {
            TextBlock block;
            if (metricValues.TryGetValue(key, out block))
                block.Text = value;
        }

        /// <summary>
        /// Executes the create panel operation.
        /// </summary>
        private Border CreatePanel()
        {
            return new Border
            {
                Background = BrushFromHex("#F7FAFC"),
                BorderBrush = BrushFromHex("#D8DEE9"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14)
            };
        }

        /// <summary>
        /// Executes the trim operation.
        /// </summary>
        private void Trim<T>(List<T> values)
        {
            while (values.Count > maxLength)
                values.RemoveAt(0);
        }

        /// <summary>
        /// Executes the run on ui operation.
        /// </summary>
        private void RunOnUi(Action action)
        {
            if (isClosed)
                return;
            if (Dispatcher.CheckAccess())
                action();
            else
                Dispatcher.BeginInvoke(action);
        }

        /// <summary>
        /// Executes the brush from hex operation.
        /// </summary>
        private static SolidColorBrush BrushFromHex(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    /// <summary>
    /// Represents the metric chart component.
    /// </summary>
    internal class MetricChart : FrameworkElement
    {
        /// <summary>
        /// Stores the values a value.
        /// </summary>
        private readonly List<double> valuesA = new List<double>();
        /// <summary>
        /// Stores the values b value.
        /// </summary>
        private readonly List<double> valuesB = new List<double>();

        /// <summary>
        /// Gets or sets the title value.
        /// </summary>
        public string Title { get; set; }
        /// <summary>
        /// Gets or sets the series a name value.
        /// </summary>
        public string SeriesAName { get; set; }
        /// <summary>
        /// Gets or sets the series b name value.
        /// </summary>
        public string SeriesBName { get; set; }
        /// <summary>
        /// Gets or sets the series a brush value.
        /// </summary>
        public Brush SeriesABrush { get; set; }
        /// <summary>
        /// Gets or sets the series b brush value.
        /// </summary>
        public Brush SeriesBBrush { get; set; }

        /// <summary>
        /// Executes the metric chart operation.
        /// </summary>
        public MetricChart()
        {
            MinHeight = 180;
            SeriesABrush = Brushes.Teal;
            SeriesBBrush = Brushes.IndianRed;
        }

        /// <summary>
        /// Executes the set values operation.
        /// </summary>
        public void SetValues(IEnumerable<double> first, IEnumerable<double> second)
        {
            valuesA.Clear();
            valuesA.AddRange(first);
            valuesB.Clear();
            valuesB.AddRange(second);
            InvalidateVisual();
        }

        /// <summary>
        /// Executes the on render operation.
        /// </summary>
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            Rect bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRoundedRectangle(BrushFromHex("#F7FAFC"), new Pen(BrushFromHex("#D8DEE9"), 1), bounds, 6, 6);

            double left = 46;
            double top = 42;
            double right = Math.Max(left + 1, ActualWidth - 16);
            double bottom = Math.Max(top + 1, ActualHeight - 30);
            Rect plot = new Rect(left, top, right - left, bottom - top);

            DrawText(dc, Title ?? string.Empty, 14, FontWeights.SemiBold, BrushFromHex("#172033"), new Point(14, 12));
            DrawLegend(dc, plot);
            DrawGrid(dc, plot);

            double max = 1;
            foreach (double value in valuesA)
                max = Math.Max(max, value);
            foreach (double value in valuesB)
                max = Math.Max(max, value);

            DrawText(dc, max.ToString("0.##", CultureInfo.InvariantCulture), 11, FontWeights.Normal, BrushFromHex("#718096"), new Point(10, top - 7));
            DrawText(dc, "0", 11, FontWeights.Normal, BrushFromHex("#718096"), new Point(28, bottom - 8));
            DrawSeries(dc, valuesA, plot, max, SeriesABrush, 2);
            DrawSeries(dc, valuesB, plot, max, SeriesBBrush, 2);
        }

        /// <summary>
        /// Executes the draw legend operation.
        /// </summary>
        private void DrawLegend(DrawingContext dc, Rect plot)
        {
            double x = Math.Max(14, ActualWidth - 210);
            DrawLegendItem(dc, x, 16, SeriesABrush, SeriesAName);
            DrawLegendItem(dc, x + 96, 16, SeriesBBrush, SeriesBName);
        }

        /// <summary>
        /// Executes the draw legend item operation.
        /// </summary>
        private void DrawLegendItem(DrawingContext dc, double x, double y, Brush brush, string text)
        {
            dc.DrawRectangle(brush, null, new Rect(x, y + 4, 16, 3));
            DrawText(dc, text ?? string.Empty, 11, FontWeights.Normal, BrushFromHex("#4A5568"), new Point(x + 22, y - 2));
        }

        /// <summary>
        /// Executes the draw grid operation.
        /// </summary>
        private void DrawGrid(DrawingContext dc, Rect plot)
        {
            Pen gridPen = new Pen(BrushFromHex("#E2E8F0"), 1);
            for (int i = 0; i <= 4; i++)
            {
                double y = plot.Top + plot.Height * i / 4;
                dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            }
            dc.DrawLine(new Pen(BrushFromHex("#CBD5E0"), 1), new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
            dc.DrawLine(new Pen(BrushFromHex("#CBD5E0"), 1), new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        }

        /// <summary>
        /// Executes the draw series operation.
        /// </summary>
        private void DrawSeries(DrawingContext dc, List<double> values, Rect plot, double max, Brush brush, double thickness)
        {
            if (values.Count == 0)
                return;

            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                for (int i = 0; i < values.Count; i++)
                {
                    double x = values.Count == 1 ? plot.Left : plot.Left + plot.Width * i / (values.Count - 1);
                    double y = plot.Bottom - (Math.Max(0, values[i]) / max) * plot.Height;
                    Point point = new Point(x, y);
                    if (i == 0)
                        context.BeginFigure(point, false, false);
                    else
                        context.LineTo(point, true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(brush, thickness), geometry);
        }

        /// <summary>
        /// Executes the draw text operation.
        /// </summary>
        private static void DrawText(DrawingContext dc, string text, double size, FontWeight weight, Brush brush, Point point)
        {
            FormattedText formatted = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
                size,
                brush,
                VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? new Window()).PixelsPerDip);
            dc.DrawText(formatted, point);
        }

        /// <summary>
        /// Executes the brush from hex operation.
        /// </summary>
        private static SolidColorBrush BrushFromHex(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }
}
#endregion
