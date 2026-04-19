using System.Diagnostics;

namespace FogSwitcher;

internal sealed class MainForm : Form
{
    private const int TitleBarHeight = 48;
    private const int TitleBarIconSize = 32;
    private const float TitleBarIconContentScale = 0.98f;

    private readonly DeadByQueueClient _queueClient = new();
    private readonly LatencyProbeService _latencyProbe = new();
    private readonly HostsFileSelectorService _hostsService = new();
    private readonly GitHubReleaseUpdateService _updateService = new();
    private readonly Dictionary<string, RegionCardView> _regionCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long?> _latencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _latencyTimer = new();
    private readonly System.Windows.Forms.Timer _queueTimer = new();

    private DeadByQueueSnapshot? _queueSnapshot;
    private HashSet<string> _appliedAllowedCodes = new(StringComparer.OrdinalIgnoreCase);

    private Label _adminBadge = null!;
    private Label _selectionBadge = null!;
    private Label _queueBadge = null!;
    private Label _lastUpdatedLabel = null!;
    private Label _eventLabel = null!;
    private Label _footerLabel = null!;
    private ComboBox _queueModeCombo = null!;
    private TableLayoutPanel _sectionsTable = null!;
    private IMessageFilter? _wheelFilter;

    private bool _refreshingLatency;
    private bool _refreshingQueues;
    private bool _loadingSelection;

    // Drag state (borderless window)
    private bool _isDragging;
    private Point _dragOffset;

    // Color palette
    private static readonly Color BgColor       = Color.FromArgb(13, 15, 18);
    private static readonly Color SurfaceColor  = Color.FromArgb(20, 23, 27);
    private static readonly Color TitleBarColor = Color.FromArgb(9, 11, 14);
    private static readonly Color SepColor      = Color.FromArgb(34, 38, 43);
    private static readonly Color TextColor     = Color.FromArgb(220, 215, 205);
    private static readonly Color MutedColor    = Color.FromArgb(130, 138, 150);
    private static readonly Color AccentColor   = Color.FromArgb(165, 37, 37);
    private static readonly Color SuccessColor  = Color.FromArgb(58, 181, 127);
    private static readonly Color WarningColor  = Color.FromArgb(236, 186, 71);
    private static readonly Color DangerColor   = Color.FromArgb(226, 85, 85);
    private static readonly Color NeutralColor  = Color.FromArgb(50, 56, 64);
    private static readonly Color InfoColor     = Color.FromArgb(69, 122, 194);

    public MainForm()
    {
        Text = "Fog Switcher";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(820, 560);
        BackColor = BgColor;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 8.75f, FontStyle.Regular, GraphicsUnit.Point);
        LoadApplicationIcon();

        InitializeUi();
        LoadAppliedSelection();

        Shown += async (_, _) =>
        {
            await RefreshAllAsync(showQueueErrors: false);
            await CheckForUpdatesOnStartupAsync();
        };
        FormClosing += OnFormClosing;
    }

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    private void InitializeUi()
    {
        var border = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SepColor,
            Padding = new Padding(1)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = Padding.Empty,
            BackColor = BgColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // title bar
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // header
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // toolbar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // content
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // footer

        root.Controls.Add(BuildTitleBar(), 0, 0);
        root.Controls.Add(BuildHeaderPanel(), 0, 1);
        root.Controls.Add(BuildToolbarPanel(), 0, 2);
        root.Controls.Add(BuildSectionsPanel(), 0, 3);
        root.Controls.Add(BuildFooterPanel(), 0, 4);

        border.Controls.Add(root);
        Controls.Add(border);

        _latencyTimer.Interval = 5_000;
        _latencyTimer.Tick += async (_, _) => await RefreshLatenciesAsync();

        _queueTimer.Interval = 120_000;
        _queueTimer.Tick += async (_, _) => await RefreshQueuesAsync(showErrors: false);
    }

    private Control BuildTitleBar()
    {
        var titleBarIconImage = CreateTitleBarIconImage();

        var bar = new Panel
        {
            Dock = DockStyle.Fill,
            Height = TitleBarHeight,
            BackColor = TitleBarColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var bottomLine = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = SepColor
        };

        var titleArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = titleBarIconImage is null ? 1 : 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(12, 0, 0, 0)
        };
        if (titleBarIconImage is not null)
        {
            titleArea.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }
        titleArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        if (titleBarIconImage is not null)
        {
            var iconBox = new PictureBox
            {
                Size = new Size(TitleBarIconSize, TitleBarIconSize),
                Margin = new Padding(0, 8, 10, 8),
                BackColor = Color.Transparent,
                Image = titleBarIconImage,
                SizeMode = PictureBoxSizeMode.Zoom
            };

            titleArea.Controls.Add(iconBox, 0, 0);
            AttachDrag(iconBox);
        }

        var titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Fog Switcher",
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = TextColor,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = Padding.Empty,
            Margin = new Padding(0)
        };

        var closeBtn = CreateTitleButton("✕");
        closeBtn.Click += (_, _) => Close();
        closeBtn.MouseEnter += (_, _) => { closeBtn.BackColor = Color.FromArgb(196, 43, 28); closeBtn.ForeColor = Color.White; };
        closeBtn.MouseLeave += (_, _) => { closeBtn.BackColor = Color.Transparent; closeBtn.ForeColor = MutedColor; };

        var minimizeBtn = CreateTitleButton("─");
        minimizeBtn.Click += (_, _) => WindowState = FormWindowState.Minimized;
        minimizeBtn.MouseEnter += (_, _) => minimizeBtn.BackColor = Color.FromArgb(38, 43, 50);
        minimizeBtn.MouseLeave += (_, _) => minimizeBtn.BackColor = Color.Transparent;

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        buttonsPanel.Controls.Add(minimizeBtn);
        buttonsPanel.Controls.Add(closeBtn);

        titleArea.Controls.Add(titleLabel, titleBarIconImage is null ? 0 : 1, 0);

        bar.Controls.Add(bottomLine);
        bar.Controls.Add(buttonsPanel);
        bar.Controls.Add(titleArea);

        AttachDrag(bar);
        AttachDrag(titleArea);
        AttachDrag(titleLabel);

        return bar;
    }

    private void LoadApplicationIcon()
    {
        using var extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (extractedIcon is not null)
        {
            Icon = (Icon)extractedIcon.Clone();
        }
    }

    private Bitmap? CreateTitleBarIconImage()
    {
        using var resourceStream = typeof(MainForm).Assembly.GetManifestResourceStream("FogSwitcher.Assets.app_icon.png");
        if (resourceStream is not null)
        {
            using var sourceBitmap = (Bitmap)Bitmap.FromStream(resourceStream);
            return ResizeTitleBarIcon(sourceBitmap);
        }

        if (Icon is null)
            return null;

        using var fallbackBitmap = Icon.ToBitmap();
        return ResizeTitleBarIcon(fallbackBitmap);
    }

    private static Bitmap ResizeTitleBarIcon(Bitmap sourceBitmap)
    {
        var visibleBounds = GetVisibleBounds(sourceBitmap);
        var bitmap = new Bitmap(TitleBarIconSize, TitleBarIconSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        var contentSize = Math.Max(1, (int)Math.Round(TitleBarIconSize * TitleBarIconContentScale));
        var scale = Math.Min(
            contentSize / (double)Math.Max(visibleBounds.Width, 1),
            contentSize / (double)Math.Max(visibleBounds.Height, 1));
        var drawWidth = Math.Max(1, (int)Math.Round(visibleBounds.Width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(visibleBounds.Height * scale));
        var offsetX = (bitmap.Width - drawWidth) / 2;
        var offsetY = (bitmap.Height - drawHeight) / 2;

        graphics.DrawImage(
            sourceBitmap,
            new Rectangle(offsetX, offsetY, drawWidth, drawHeight),
            visibleBounds,
            GraphicsUnit.Pixel);

        RemoveNearWhiteMatte(bitmap);
        return bitmap;
    }

    private static Rectangle GetVisibleBounds(Bitmap bitmap)
    {
        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A <= 0)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0 || maxY < 0
            ? new Rectangle(0, 0, bitmap.Width, bitmap.Height)
            : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static void RemoveNearWhiteMatte(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A == 0)
                    continue;

                var max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                var min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                var average = (pixel.R + pixel.G + pixel.B) / 3;
                var isNearNeutral = max - min <= 12;

                if (!isNearNeutral || average < 240)
                    continue;

                var alpha = average >= 249
                    ? 0
                    : (int)Math.Round(255d * (249 - average) / 9d);

                if (alpha <= 0)
                {
                    bitmap.SetPixel(x, y, Color.Transparent);
                    continue;
                }

                bitmap.SetPixel(
                    x,
                    y,
                    Color.FromArgb(
                        alpha,
                        RemoveWhiteBackdrop(pixel.R, alpha),
                        RemoveWhiteBackdrop(pixel.G, alpha),
                        RemoveWhiteBackdrop(pixel.B, alpha)));
            }
        }
    }

    private static byte RemoveWhiteBackdrop(byte channel, int alpha)
    {
        if (alpha >= 255)
            return channel;

        var opacity = alpha / 255d;
        var value = (channel - (255d * (1d - opacity))) / opacity;
        return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }

    // Compact header: title + badges on one line, status info on a smaller second line.
    private Control BuildHeaderPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = SurfaceColor,
            Padding = new Padding(8, 6, 8, 6),
            Margin = new Padding(8, 5, 8, 3)
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Row 0: title + badges inline
        var topRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };

        var title = new Label
        {
            AutoSize = true,
            Text = "DBD Server Selector",
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = TextColor,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 12, 0)
        };

        _adminBadge     = CreateBadgeLabel();
        _selectionBadge = CreateBadgeLabel();
        _queueBadge     = CreateBadgeLabel();

        topRow.Controls.Add(title);
        topRow.Controls.Add(_adminBadge);
        topRow.Controls.Add(_selectionBadge);
        topRow.Controls.Add(_queueBadge);

        // Row 1: status info (small, muted)
        var bottomRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 0, 0)
        };

        _lastUpdatedLabel = new Label
        {
            AutoSize = true,
            ForeColor = MutedColor,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 10, 0),
            Font = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point)
        };
        _eventLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(210, 185, 120),
            BackColor = Color.Transparent,
            MaximumSize = new Size(500, 0),
            Font = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point)
        };

        bottomRow.Controls.Add(_lastUpdatedLabel);
        bottomRow.Controls.Add(_eventLabel);

        stack.Controls.Add(topRow);
        stack.Controls.Add(bottomRow);

        panel.Controls.Add(stack);
        UpdateBadges();
        return panel;
    }

    private Control BuildToolbarPanel()
    {
        var flow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            BackColor = BgColor,
            Margin = new Padding(8, 0, 8, 2),
            Padding = new Padding(0, 2, 0, 2)
        };

        flow.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Queue:",
            ForeColor = MutedColor,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 4, 0)
        });

        _queueModeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 105,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 3, 10, 0),
            IntegralHeight = false
        };
        _queueModeCombo.Items.Add(QueueMode.Live);
        _queueModeCombo.Items.Add(QueueMode.LiveEvent);
        _queueModeCombo.SelectedIndex = 0;
        _queueModeCombo.Format += (_, e) =>
        {
            if (e.ListItem is QueueMode mode)
                e.Value = mode.ToDisplayName();
        };
        _queueModeCombo.SelectedIndexChanged += (_, _) => RefreshCards();

        var sep = new Panel
        {
            BackColor = SepColor,
            Size = new Size(1, 22),
            Margin = new Padding(0, 3, 10, 0)
        };

        var refreshBtn      = BuildButton("Refresh",         AccentColor,                  TextColor);
        var bestPingBtn     = BuildButton("Best Ping",       InfoColor,                    TextColor);
        var lowestKillerBtn = BuildButton("Lowest Killer",   Color.FromArgb(170, 95, 50),  TextColor);
        var lowestSurvBtn   = BuildButton("Lowest Survivor", Color.FromArgb(45, 120, 105), TextColor);
        var applyBtn        = BuildButton("Apply",           SuccessColor,                 Color.Black);
        var clearBtn        = BuildButton("Clear",           NeutralColor,                 TextColor);

        refreshBtn.Click      += async (_, _) => await RefreshAllAsync(showQueueErrors: true);
        bestPingBtn.Click     += (_, _) => SelectBestPingRegion();
        lowestKillerBtn.Click += (_, _) => SelectLowestQueueRegions(q => q.KillerSeconds, "killer");
        lowestSurvBtn.Click   += (_, _) => SelectLowestQueueRegions(q => q.SurvivorSeconds, "survivor");
        applyBtn.Click        += (_, _) => ApplySelection();
        clearBtn.Click        += (_, _) => ClearSelection();

        flow.Controls.Add(_queueModeCombo);
        flow.Controls.Add(sep);
        flow.Controls.Add(refreshBtn);
        flow.Controls.Add(bestPingBtn);
        flow.Controls.Add(lowestKillerBtn);
        flow.Controls.Add(lowestSurvBtn);
        flow.Controls.Add(applyBtn);
        flow.Controls.Add(clearBtn);

        return flow;
    }

    private Control BuildSectionsPanel()
    {
        // _sectionsTable is NOT docked — SmoothScrollPane manages its position.
        _sectionsTable = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = BgColor,
            Location = Point.Empty
        };
        _sectionsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        foreach (var groupName in DbdRegionCatalog.GroupOrder)
        {
            var regions = DbdRegionCatalog.VisibleRegions
                .Where(r => string.Equals(r.GroupName, groupName, StringComparison.Ordinal))
                .ToList();
            if (regions.Count == 0)
                continue;

            _sectionsTable.Controls.Add(BuildSectionControl(groupName, regions));
        }

        var pane = new SmoothScrollPane
        {
            Dock = DockStyle.Fill,
            BackColor = BgColor,
            Margin = new Padding(8, 0, 8, 0)
        };
        pane.SetContent(_sectionsTable);

        _wheelFilter = new WheelFilter(pane);
        Application.AddMessageFilter(_wheelFilter);

        return pane;
    }

    private Control BuildSectionControl(string groupName, IReadOnlyList<DbdRegion> regions)
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 8, 8),
            BackColor = BgColor
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var header = new Label
        {
            AutoSize = true,
            Text = groupName.ToUpperInvariant(),
            ForeColor = MutedColor,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 7.5f, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(4, 0, 0, 3)
        };

        var separator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = SepColor,
            Margin = new Padding(0, 0, 0, 3)
        };

        var cardsTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = BgColor
        };
        cardsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        foreach (var region in regions)
        {
            var card = new RegionCardView(region);
            card.CheckedChanged += OnRegionCardCheckedChanged;
            _regionCards[region.Code] = card;
            cardsTable.Controls.Add(card.Root);
        }

        section.Controls.Add(header);
        section.Controls.Add(separator);
        section.Controls.Add(cardsTable);
        return section;
    }

    private Control BuildFooterPanel()
    {
        _footerLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(8, 3, 8, 6),
            ForeColor = MutedColor,
            BackColor = Color.Transparent,
            Text = $"{DbdRegionCatalog.VisibleRegions.Count} regions monitored  ·  Auto-refresh: ping 5s, queue 2min"
        };
        return _footerLabel;
    }

    // -------------------------------------------------------------------------
    // Drag behavior (borderless window)
    // -------------------------------------------------------------------------

    private void AttachDrag(Control ctrl)
    {
        ctrl.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _isDragging = true;
            _dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
        };
        ctrl.MouseMove += (_, _) =>
        {
            if (_isDragging)
                Location = new Point(Cursor.Position.X - _dragOffset.X, Cursor.Position.Y - _dragOffset.Y);
        };
        ctrl.MouseUp    += (_, _) => _isDragging = false;
        ctrl.MouseLeave += (_, _) => _isDragging = false;
    }

    // -------------------------------------------------------------------------
    // Event handlers
    // -------------------------------------------------------------------------

    private void OnRegionCardCheckedChanged(object? sender, EventArgs e)
    {
        if (_loadingSelection)
            return;

        RefreshCards();
        UpdateBadges();
    }

    // -------------------------------------------------------------------------
    // Async data refresh
    // -------------------------------------------------------------------------

    private async Task RefreshAllAsync(bool showQueueErrors)
    {
        await Task.WhenAll(
            RefreshQueuesAsync(showQueueErrors),
            RefreshLatenciesAsync());
    }

    private async Task RefreshQueuesAsync(bool showErrors)
    {
        if (_refreshingQueues)
            return;

        _refreshingQueues = true;
        try
        {
            SetBadgeStyle(_queueBadge, "Queue: refreshing", NeutralColor, TextColor);
            _queueSnapshot = await _queueClient.FetchSnapshotAsync();
            _queueTimer.Start();
            RefreshCards();
            UpdateBadges();
        }
        catch (Exception ex)
        {
            if (showErrors)
            {
                MessageBox.Show(
                    this,
                    "Failed to refresh queue data.\n\n" + ex.Message,
                    "Dead by Queue",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            SetBadgeStyle(_queueBadge, "Queue unavailable", DangerColor, Color.White);
            _lastUpdatedLabel.Text = "Queue: offline or unreachable.";
            _eventLabel.Text = string.Empty;
        }
        finally
        {
            _refreshingQueues = false;
        }
    }

    private async Task RefreshLatenciesAsync()
    {
        if (_refreshingLatency)
            return;

        _refreshingLatency = true;
        try
        {
            var measured = await _latencyProbe.MeasureAsync(DbdRegionCatalog.VisibleRegions);
            foreach (var pair in measured)
                _latencies[pair.Key] = pair.Value;

            _latencyTimer.Start();
            RefreshCards();
            UpdateBadges();

            _footerLabel.Text =
                $"{DbdRegionCatalog.VisibleRegions.Count} regions monitored  ·  " +
                $"Ping: {DateTime.Now:HH:mm:ss}  ·  Auto-refresh: 5s";
        }
        finally
        {
            _refreshingLatency = false;
        }
    }

    // -------------------------------------------------------------------------
    // Selection management
    // -------------------------------------------------------------------------

    private void LoadAppliedSelection()
    {
        _appliedAllowedCodes = _hostsService.ReadAllowedRegionCodes();

        _loadingSelection = true;
        try
        {
            foreach (var region in DbdRegionCatalog.VisibleRegions)
                _regionCards[region.Code].SetChecked(_appliedAllowedCodes.Contains(region.Code));
        }
        finally
        {
            _loadingSelection = false;
        }

        RefreshCards();
        UpdateBadges();
    }

    private void RefreshCards()
    {
        var hasManagedSelection = _appliedAllowedCodes.Count > 0;

        foreach (var region in DbdRegionCatalog.VisibleRegions)
        {
            var card = _regionCards[region.Code];
            var latency = _latencies.TryGetValue(region.Code, out var value) ? value : null;
            var activeState = GetActiveState(region.Code);
            var queueTimes = GetQueueTimes(region.Code);
            var isCurrentlyAllowed = !hasManagedSelection || _appliedAllowedCodes.Contains(region.Code);
            var isPending = hasManagedSelection
                ? card.IsChecked != isCurrentlyAllowed
                : card.IsChecked;

            var statusText = BuildStatusText(region, activeState, isCurrentlyAllowed);
            var killerQueueText = BuildQueueBadgeText("Killer", queueTimes?.KillerSeconds, activeState);
            var survivorQueueText = BuildQueueBadgeText("Survivor", queueTimes?.SurvivorSeconds, activeState);
            var (killerQueueBackColor, killerQueueForeColor) = GetQueueBadgeColors(queueTimes?.KillerSeconds, isCurrentlyAllowed, activeState);
            var (survivorQueueBackColor, survivorQueueForeColor) = GetQueueBadgeColors(queueTimes?.SurvivorSeconds, isCurrentlyAllowed, activeState);
            var pingBackColor = GetLatencyBackColor(latency, isCurrentlyAllowed);
            var pingForeColor = isCurrentlyAllowed && latency.HasValue ? Color.Black : TextColor;
            var (statusBackColor, statusForeColor) = GetStatusColors(region, activeState, isCurrentlyAllowed);

            card.ApplyPresentation(
                pingText: FormatLatency(latency, isCurrentlyAllowed),
                pingBackColor: pingBackColor,
                pingForeColor: pingForeColor,
                statusText: statusText,
                statusBackColor: statusBackColor,
                statusForeColor: statusForeColor,
                killerQueueText: killerQueueText,
                killerQueueBackColor: killerQueueBackColor,
                killerQueueForeColor: killerQueueForeColor,
                survivorQueueText: survivorQueueText,
                survivorQueueBackColor: survivorQueueBackColor,
                survivorQueueForeColor: survivorQueueForeColor,
                isSelected: card.IsChecked,
                isCurrentlyAllowed: isCurrentlyAllowed,
                isPending: isPending);
        }
    }

    private void ApplySelection()
    {
        var selected = GetCheckedRegionCodes();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Check at least one region before applying.",
                "No region selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_queueSnapshot is not null &&
            selected.All(code => _queueSnapshot.ActiveRegions.TryGetValue(code, out var active) && !active))
        {
            var confirm = MessageBox.Show(this,
                "All selected regions appear inactive on Dead by Queue.\n\nApply anyway?",
                "Inactive regions", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;
        }

        if (!_hostsService.IsRunningAsAdministrator)
        {
            var elevate = MessageBox.Show(this,
                "Administrator privileges are needed to edit the hosts file.\n\nLaunch a small elevated helper now?",
                "Administrator required", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (elevate != DialogResult.Yes)
                return;

            if (!ElevatedHostsOperation.TryApplySelection(selected, out var errorMessage))
            {
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    MessageBox.Show(this, errorMessage,
                        "Apply failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            _appliedAllowedCodes = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
            RefreshCards();
            UpdateBadges();

            MessageBox.Show(this,
                "Selection applied.\n\nRestart Dead by Daylight for the change to take effect.",
                "Selection applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            _hostsService.ApplySelection(selected);
            _appliedAllowedCodes = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
            RefreshCards();
            UpdateBadges();

            MessageBox.Show(this,
                "Selection applied.\n\nRestart Dead by Daylight for the change to take effect.",
                "Selection applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(this,
                "Windows denied access to the hosts file. Try again and accept the administrator prompt.",
                "Access denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to apply the selection.\n\n" + ex.Message,
                "Apply failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearSelection()
    {
        if (!_hostsService.IsRunningAsAdministrator)
        {
            var elevate = MessageBox.Show(this,
                "Administrator privileges are needed to remove the Fog Switcher block from the hosts file.\n\nLaunch a small elevated helper now?",
                "Administrator required", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (elevate != DialogResult.Yes)
                return;

            if (!ElevatedHostsOperation.TryClearSelection(out var errorMessage))
            {
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    MessageBox.Show(this, errorMessage,
                        "Clear failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            _appliedAllowedCodes.Clear();

            _loadingSelection = true;
            try { foreach (var card in _regionCards.Values) card.SetChecked(false); }
            finally { _loadingSelection = false; }

            RefreshCards();
            UpdateBadges();

            MessageBox.Show(this,
                "The Fog Switcher block has been removed from the hosts file.\n\nOther existing lines were preserved.",
                "Locks removed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            _hostsService.ClearSelection();
            _appliedAllowedCodes.Clear();

            _loadingSelection = true;
            try { foreach (var card in _regionCards.Values) card.SetChecked(false); }
            finally { _loadingSelection = false; }

            RefreshCards();
            UpdateBadges();

            MessageBox.Show(this,
                "The Fog Switcher block has been removed from the hosts file.\n\nOther existing lines were preserved.",
                "Locks removed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to clean the hosts file.\n\n" + ex.Message,
                "Clear failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var update = await _updateService.GetAvailableUpdateAsync();
            if (update is null || IsDisposed || Disposing)
            {
                return;
            }

            var destinationLabel = update.OpensReleasePage ? "release page" : "download page";
            var result = MessageBox.Show(this,
                $"Version {update.VersionText} is available.\n\nCurrent version: {GitHubReleaseUpdateService.CurrentVersionText}\n\nOpen the {destinationLabel} now?",
                "Update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);

            if (result != DialogResult.Yes)
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = update.DownloadUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Update checks are best-effort only.
        }
    }

    private void SelectBestPingRegion()
    {
        var bestRegion = DbdRegionCatalog.VisibleRegions
            .Select(r => new { Region = r, Latency = _latencies.TryGetValue(r.Code, out var l) ? l : null, Active = GetActiveState(r.Code) })
            .Where(x => x.Latency.HasValue && x.Active != false)
            .OrderBy(x => x.Latency!.Value)
            .Select(x => x.Region)
            .FirstOrDefault();

        if (bestRegion is null)
        {
            MessageBox.Show(this, "No usable ping data available. Try refreshing first.",
                "Best ping unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SetSingleCheckedRegion(bestRegion.Code);
    }

    private void SelectLowestQueueRegions(Func<QueueRoleTimes, int?> queueSelector, string roleLabel)
    {
        var candidates = DbdRegionCatalog.VisibleRegions
            .Select(r => new { Region = r, Seconds = GetQueueTimes(r.Code) is { } qt ? queueSelector(qt) : null, Active = GetActiveState(r.Code) })
            .Where(x => x.Seconds.HasValue && x.Active != false)
            .ToList();

        if (candidates.Count == 0)
        {
            MessageBox.Show(this, $"No usable {roleLabel} queue data available. Try refreshing first.",
                $"Lowest {roleLabel}", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var lowest = candidates.Min(x => x.Seconds!.Value);
        SetCheckedRegions(candidates.Where(x => x.Seconds == lowest).Select(x => x.Region.Code));
    }

    private void SetSingleCheckedRegion(string code) => SetCheckedRegions([code]);

    private void SetCheckedRegions(IEnumerable<string> codes)
    {
        var set = codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _loadingSelection = true;
        try { foreach (var r in DbdRegionCatalog.VisibleRegions) _regionCards[r.Code].SetChecked(set.Contains(r.Code)); }
        finally { _loadingSelection = false; }
        RefreshCards();
        UpdateBadges();
    }

    private HashSet<string> GetCheckedRegionCodes() =>
        _regionCards.Values.Where(c => c.IsChecked).Select(c => c.Region.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private QueueRoleTimes? GetQueueTimes(string regionCode)
    {
        if (_queueSnapshot is null) return null;
        var mode = _queueModeCombo.SelectedItem is QueueMode m ? m : QueueMode.Live;
        if (_queueSnapshot.QueueTimes.TryGetValue(mode, out var qs) && qs.TryGetValue(regionCode, out var t)) return t;
        if (_queueSnapshot.QueueTimes.TryGetValue(QueueMode.Live, out var fb) && fb.TryGetValue(regionCode, out var ft)) return ft;
        return null;
    }

    private bool? GetActiveState(string regionCode)
    {
        if (_queueSnapshot is null) return null;
        return _queueSnapshot.ActiveRegions.TryGetValue(regionCode, out var active) ? active : null;
    }

    // -------------------------------------------------------------------------
    // Badge / status helpers
    // -------------------------------------------------------------------------

    private void UpdateBadges()
    {
        var checkedCodes = GetCheckedRegionCodes();
        var pendingChanges = !_appliedAllowedCodes.SetEquals(checkedCodes);

        SetBadgeStyle(_adminBadge,
            _hostsService.IsRunningAsAdministrator ? "Hosts ready" : "No admin",
            _hostsService.IsRunningAsAdministrator ? SuccessColor : WarningColor,
            Color.Black);

        if (checkedCodes.Count == 0 && _appliedAllowedCodes.Count == 0)
            SetBadgeStyle(_selectionBadge, "No region", NeutralColor, TextColor);
        else if (pendingChanges)
            SetBadgeStyle(_selectionBadge, $"Pending ({checkedCodes.Count})", WarningColor, Color.Black);
        else
            SetBadgeStyle(_selectionBadge,
                _appliedAllowedCodes.Count > 0 ? $"Lock active ({_appliedAllowedCodes.Count})" : "No lock",
                InfoColor, TextColor);

        if (_queueSnapshot is null)
        {
            SetBadgeStyle(_queueBadge, "Queue ?", NeutralColor, TextColor);
            _lastUpdatedLabel.Text = "Waiting for first refresh.";
            _eventLabel.Text = string.Empty;
            return;
        }

        SetBadgeStyle(_queueBadge,
            _queueSnapshot.IsOnline ? "Queue online" : "Queue offline",
            _queueSnapshot.IsOnline ? Color.FromArgb(42, 109, 85) : DangerColor,
            TextColor);

        _lastUpdatedLabel.Text = _queueSnapshot.LastUpdated is { } updated
            ? $"Queue: {updated:HH:mm:ss}"
            : "Queue loaded, no timestamp.";

        _eventLabel.Text = string.IsNullOrWhiteSpace(_queueSnapshot.EventSummary)
            ? string.Empty : $"Event: {_queueSnapshot.EventSummary}";
    }

    private static string FormatLatency(long? latency, bool allowed) =>
        !allowed ? "Locked" : latency.HasValue ? $"{latency.Value} ms" : "n/a";

    private static string BuildStatusText(DbdRegion region, bool? isActive, bool allowed)
    {
        if (!allowed) return "Locked";
        if (isActive == false) return "Offline";
        if (region.IsTemporary) return "Temporary";
        return isActive == true ? "Online" : "Checking";
    }

    private static string BuildQueueBadgeText(string role, int? seconds, bool? isActive) =>
        (!seconds.HasValue && isActive == false) ? $"{role} off" : $"{role} {FormatQueueSeconds(seconds)}";

    private static string FormatQueueSeconds(int? seconds)
    {
        if (!seconds.HasValue) return "--";
        if (seconds.Value < 60) return $"{seconds.Value}s";
        var span = TimeSpan.FromSeconds(seconds.Value);
        return span.Hours > 0 ? span.ToString(@"h\h\ mm\m") : span.ToString(@"m\m\ ss\s");
    }

    private static Color GetLatencyBackColor(long? latency, bool allowed)
    {
        if (!allowed) return Color.FromArgb(60, 62, 66);
        if (!latency.HasValue) return Color.FromArgb(50, 55, 62);
        return latency.Value switch
        {
            < 60  => Color.FromArgb(58, 181, 127),
            < 100 => Color.FromArgb(236, 186, 71),
            < 160 => Color.FromArgb(239, 143, 64),
            _     => Color.FromArgb(226, 85, 85)
        };
    }

    private static (Color, Color) GetStatusColors(DbdRegion region, bool? isActive, bool allowed)
    {
        if (!allowed)          return (Color.FromArgb(80, 40, 40), TextColor);
        if (isActive == false) return (Color.FromArgb(80, 65, 30), TextColor);
        if (region.IsTemporary) return (Color.FromArgb(80, 60, 28), TextColor);
        if (isActive == true)  return (Color.FromArgb(35, 85, 62),  TextColor);
        return (Color.FromArgb(50, 55, 62), TextColor);
    }

    private static (Color, Color) GetQueueBadgeColors(int? seconds, bool allowed, bool? isActive)
    {
        if (!allowed)          return (Color.FromArgb(60, 62, 66), TextColor);
        if (isActive == false) return (Color.FromArgb(80, 65, 30), TextColor);
        if (!seconds.HasValue) return (Color.FromArgb(50, 55, 62), TextColor);
        if (seconds.Value < 30)  return (Color.FromArgb(58, 181, 127),  Color.Black);
        if (seconds.Value < 120) return (Color.FromArgb(236, 186, 71),  Color.Black);
        if (seconds.Value < 240) return (Color.FromArgb(239, 143, 64),  Color.Black);
        return (Color.FromArgb(226, 85, 85), Color.White);
    }

    private static Label CreateBadgeLabel() => new()
    {
        AutoSize = true,
        Padding = new Padding(8, 4, 8, 4),
        Margin = new Padding(0, 2, 6, 0),
        Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Regular, GraphicsUnit.Point)
    };

    private static Button BuildButton(string text, Color back, Color fore) => new()
    {
        AutoSize = true, Text = text, BackColor = back, ForeColor = fore,
        FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 },
        Margin = new Padding(0, 0, 4, 0), Padding = new Padding(8, 4, 8, 4),
        Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Regular, GraphicsUnit.Point),
        Cursor = Cursors.Hand
    };

    private static Button CreateTitleButton(string symbol) => new()
    {
        Text = symbol, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 },
        BackColor = Color.Transparent, ForeColor = MutedColor,
        Size = new Size(42, TitleBarHeight - 1), Cursor = Cursors.Hand, TabStop = false,
        Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point)
    };

    private static void SetBadgeStyle(Label lbl, string text, Color back, Color fore)
    {
        lbl.Text = text; lbl.BackColor = back; lbl.ForeColor = fore;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_wheelFilter != null)
            Application.RemoveMessageFilter(_wheelFilter);

        _latencyTimer.Stop();
        _queueTimer.Stop();
        _latencyTimer.Dispose();
        _queueTimer.Dispose();
        _queueClient.Dispose();
        _updateService.Dispose();
    }

    // =========================================================================
    // Smooth scroll pane — custom scrollbar + real-time scroll
    // =========================================================================

    private sealed class SmoothScrollPane : Panel
    {
        private readonly Panel _viewport;
        private readonly FlatVScrollBar _vbar;
        private Control? _content;

        // Buffer all descendant paints into one back buffer → no mid-scroll flicker.
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }

        public SmoothScrollPane()
        {
            DoubleBuffered = true;
            _vbar = new FlatVScrollBar { Dock = DockStyle.Right, Width = 8 };
            _viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = false };

            _vbar.ValueChanged += (_, _) => ApplyScroll();
            _viewport.Resize   += (_, _) => Sync();

            // Dock.Right is processed before Dock.Fill — add viewport first (index 0), vbar second (index 1)
            Controls.Add(_viewport);
            Controls.Add(_vbar);
        }

        public void SetContent(Control content)
        {
            _content = content;
            _viewport.Controls.Clear();
            _viewport.Controls.Add(content);
            content.Location = Point.Empty;
            content.SizeChanged += (_, _) => Sync();
            Sync();
        }

        public void ScrollBy(int delta)
        {
            _vbar.Value += delta;
        }

        private void Sync()
        {
            if (_content is null) return;
            _content.Width = Math.Max(1, _viewport.Width);
            var max = Math.Max(0, _content.Height - _viewport.Height);
            _vbar.SetMaximum(max);
            _vbar.Visible = max > 0;
            ApplyScroll();
        }

        private void ApplyScroll()
        {
            if (_content is null) return;
            _content.Top = -_vbar.Value;
        }
    }

    // =========================================================================
    // Custom flat scrollbar
    // =========================================================================

    private sealed class FlatVScrollBar : Control
    {
        private int _value;
        private int _maximum;
        private bool _hovered;
        private bool _dragging;
        private int _dragStartY;
        private int _dragStartValue;

        private static readonly Color TrackColor      = Color.FromArgb(13, 15, 18);
        private static readonly Color ThumbNormal     = Color.FromArgb(48, 54, 63);
        private static readonly Color ThumbHover      = Color.FromArgb(68, 76, 88);
        private static readonly Color ThumbDrag       = Color.FromArgb(90, 102, 118);

        public event EventHandler? ValueChanged;

        public FlatVScrollBar() => DoubleBuffered = true;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => _value;
            set
            {
                value = Math.Clamp(value, 0, _maximum);
                if (_value == value) return;
                _value = value;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SetMaximum(int max)
        {
            _maximum = Math.Max(0, max);
            Value = Math.Min(_value, _maximum);
            Invalidate();
        }

        // Thumb proportional height relative to track
        private int ThumbH => _maximum <= 0 ? Height
            : Math.Max(24, (int)((long)Height * Height / ((long)Height + _maximum)));

        // Thumb top pixel position
        private int ThumbY => _maximum <= 0 ? 0
            : (int)((long)_value * (Height - ThumbH) / _maximum);

        private Rectangle ThumbRect => new(1, ThumbY, Width - 2, ThumbH);

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(TrackColor);
            if (_maximum > 0)
            {
                var color = _dragging ? ThumbDrag : _hovered ? ThumbHover : ThumbNormal;
                using var br = new SolidBrush(color);
                e.Graphics.FillRectangle(br, ThumbRect);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                var track = Height - ThumbH;
                if (track > 0)
                    Value = _dragStartValue + (int)((long)(e.Y - _dragStartY) * _maximum / track);
            }
            else
            {
                var over = ThumbRect.Contains(e.Location);
                if (over != _hovered) { _hovered = over; Invalidate(); }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var r = ThumbRect;
                if (r.Contains(e.Location))
                {
                    _dragging = true; _dragStartY = e.Y; _dragStartValue = _value;
                    Capture = true; Invalidate();
                }
                else
                {
                    // Click on track: jump
                    var track = Height - ThumbH;
                    if (track > 0)
                        Value = (int)((long)Math.Max(0, e.Y - ThumbH / 2) * _maximum / track);
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging) { _dragging = false; Capture = false; Invalidate(); }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnSizeChanged(EventArgs e) { Invalidate(); base.OnSizeChanged(e); }
    }

    // =========================================================================
    // Mouse-wheel message filter — routes scroll wheel to SmoothScrollPane
    // =========================================================================

    private sealed class WheelFilter : IMessageFilter
    {
        private const int WmMouseWheel = 0x020A;
        private readonly SmoothScrollPane _pane;

        public WheelFilter(SmoothScrollPane pane) => _pane = pane;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmMouseWheel) return false;
            var pos = Cursor.Position;
            if (!new Rectangle(_pane.PointToScreen(Point.Empty), _pane.Size).Contains(pos)) return false;

            var delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            _pane.ScrollBy(-delta / 120 * 50);
            return true; // message consumed
        }
    }
}
