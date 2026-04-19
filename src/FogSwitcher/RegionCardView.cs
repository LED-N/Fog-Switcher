namespace FogSwitcher;

internal sealed class RegionCardView
{
    private readonly Panel _row;
    private readonly Panel _strip;
    private readonly CheckBox _checkbox;
    private readonly Label _nameLabel;
    private readonly Label _pingBadge;
    private readonly Label _statusBadge;
    private readonly Label _killerBadge;
    private readonly Label _survivorBadge;
    private bool _suppressEvents;

    private static readonly Color RowNormal   = Color.FromArgb(20, 23, 27);
    private static readonly Color RowSelected = Color.FromArgb(22, 30, 46);
    private static readonly Color RowPending  = Color.FromArgb(30, 27, 16);
    private static readonly Color RowLocked   = Color.FromArgb(32, 19, 19);
    private static readonly Color StripNone   = Color.FromArgb(32, 36, 40);
    private static readonly Color StripBlue   = Color.FromArgb(69, 122, 194);
    private static readonly Color StripYellow = Color.FromArgb(236, 186, 71);
    private static readonly Color StripRed    = Color.FromArgb(180, 60, 60);

    public RegionCardView(DbdRegion region)
    {
        Region = region;

        _row = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Margin = new Padding(0, 0, 0, 3),
            BackColor = RowNormal,
            Cursor = Cursors.Hand
        };

        _strip = new Panel
        {
            Dock = DockStyle.Left,
            Width = 3,
            BackColor = StripNone
        };

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 5, 8, 5),
            BackColor = Color.Transparent
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _checkbox = new CheckBox
        {
            AutoSize = true,
            Margin = new Padding(0, 2, 6, 0),
            BackColor = Color.Transparent
        };
        _checkbox.CheckedChanged += (_, _) =>
        {
            if (!_suppressEvents)
                CheckedChanged?.Invoke(this, EventArgs.Empty);
        };

        _nameLabel = new Label
        {
            AutoSize = true,
            Text = region.DisplayName,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(220, 215, 205),
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 0)
        };

        _pingBadge     = CreateBadge();
        _statusBadge   = CreateBadge();
        _killerBadge   = CreateBadge();
        _survivorBadge = CreateBadge();

        var queueFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 3, 0, 0),
            BackColor = Color.Transparent
        };
        queueFlow.Controls.Add(_killerBadge);
        queueFlow.Controls.Add(_survivorBadge);

        layout.Controls.Add(_checkbox, 0, 0);
        layout.Controls.Add(_nameLabel, 1, 0);
        layout.Controls.Add(_pingBadge, 2, 0);
        layout.Controls.Add(_statusBadge, 3, 0);
        layout.Controls.Add(queueFlow, 1, 1);
        layout.SetColumnSpan(queueFlow, 3);

        content.Controls.Add(layout);
        _row.Controls.Add(content);
        _row.Controls.Add(_strip);

        Root = _row;

        ApplyPresentation(
            pingText: "--",
            pingBackColor: Color.FromArgb(48, 53, 60),
            pingForeColor: Color.FromArgb(155, 163, 173),
            statusText: "...",
            statusBackColor: Color.FromArgb(48, 53, 60),
            statusForeColor: Color.FromArgb(155, 163, 173),
            killerQueueText: "Killer --",
            killerQueueBackColor: Color.FromArgb(48, 53, 60),
            killerQueueForeColor: Color.FromArgb(155, 163, 173),
            survivorQueueText: "Survivor --",
            survivorQueueBackColor: Color.FromArgb(48, 53, 60),
            survivorQueueForeColor: Color.FromArgb(155, 163, 173),
            isSelected: false,
            isCurrentlyAllowed: true,
            isPending: false);

        AttachToggleBehavior(_row);
    }

    public event EventHandler? CheckedChanged;

    public DbdRegion Region { get; }

    public Control Root { get; }

    public bool IsChecked => _checkbox.Checked;

    public void SetChecked(bool isChecked)
    {
        _suppressEvents = true;
        try
        {
            _checkbox.Checked = isChecked;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    public void ApplyPresentation(
        string pingText,
        Color pingBackColor,
        Color pingForeColor,
        string statusText,
        Color statusBackColor,
        Color statusForeColor,
        string killerQueueText,
        Color killerQueueBackColor,
        Color killerQueueForeColor,
        string survivorQueueText,
        Color survivorQueueBackColor,
        Color survivorQueueForeColor,
        bool isSelected,
        bool isCurrentlyAllowed,
        bool isPending)
    {
        SetBadge(_pingBadge, pingText, pingBackColor, pingForeColor);
        SetBadge(_statusBadge, statusText, statusBackColor, statusForeColor);
        SetBadge(_killerBadge, killerQueueText, killerQueueBackColor, killerQueueForeColor);
        SetBadge(_survivorBadge, survivorQueueText, survivorQueueBackColor, survivorQueueForeColor);

        if (isPending)
        {
            _row.BackColor = RowPending;
            _strip.BackColor = StripYellow;
        }
        else if (isSelected)
        {
            _row.BackColor = RowSelected;
            _strip.BackColor = StripBlue;
        }
        else if (!isCurrentlyAllowed)
        {
            _row.BackColor = RowLocked;
            _strip.BackColor = StripRed;
        }
        else
        {
            _row.BackColor = RowNormal;
            _strip.BackColor = StripNone;
        }
    }

    private void AttachToggleBehavior(Control control)
    {
        if (control != _checkbox)
        {
            control.Click += (_, _) => _checkbox.Checked = !_checkbox.Checked;
        }

        foreach (Control child in control.Controls)
        {
            AttachToggleBehavior(child);
        }
    }

    private static void SetBadge(Label label, string text, Color backColor, Color foreColor)
    {
        label.Text = text;
        label.BackColor = backColor;
        label.ForeColor = foreColor;
    }

    private static Label CreateBadge() => new()
    {
        AutoSize = true,
        Padding = new Padding(6, 3, 6, 3),
        Margin = new Padding(6, 0, 0, 0),
        Font = new Font("Segoe UI Semibold", 8f, FontStyle.Regular, GraphicsUnit.Point)
    };
}
