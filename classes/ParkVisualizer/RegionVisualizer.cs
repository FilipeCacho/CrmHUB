public class RegionVisualizer : Form
{
    private List<Block> _regions = new();
    private List<Block> _allParks = new(); // Store all parks
    private List<Block> _displayedParks = new(); // Store filtered parks
    private Block? _hoveredBlock;
    private readonly Font _font = new("Segoe UI", 9.5f);
    private readonly Font _smallFont;
    private readonly StringFormat _centerFormat;
    private readonly Pen _normalBorderPen;
    private readonly Pen _hoverBorderPen;
    private BufferedPanel _mainPanel;

    // Controls for park view
    private Panel? _controlPanel;
    private ComboBox? _groupingCombo;
    private TextBox? _searchBox;
    private Button? _backButton;

    // State tracking
    private bool _isInParkView = false;
    private string _currentFilter = "";
    private string _currentGroup = "All";

    //NA & EU padding
    private const int REGION_PADDING = 10;
    
    //Parks Padding
    private const int PARK_PADDING = 3;     
    private const int BlockWidth = 220;
    private const int BlockHeight = 100;

    public RegionVisualizer()
    {
        this.DoubleBuffered = true;
        this.WindowState = FormWindowState.Maximized;
        this.Text = "Parks Visualization";

        _smallFont = new Font(_font.FontFamily, _font.Size - 1);
        _centerFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        _normalBorderPen = new Pen(Color.FromArgb(50, 50, 50), 1f);
        _hoverBorderPen = new Pen(Color.White, 2f);

        InitializeControls();
        LoadRegions();
    }

    private void InitializeControls()
    {
        _mainPanel = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.White
        };
        _mainPanel.Paint += MainPanel_Paint;
        _mainPanel.MouseMove += MainPanel_MouseMove;
        _mainPanel.MouseClick += MainPanel_MouseClick;
        _mainPanel.Resize += (s, e) => RecalculateBlocks();

        this.Controls.Add(_mainPanel);
    }

    private void LoadRegions()
    {
        _regions = new List<Block>
        {
            new Block
            {
                Name = "NA Region",
                Type = "Region",
                Region = "NA",
                Size = 100,
                Color = Color.FromArgb(135, 206, 235),
                Details = "North America Region"
            },
            new Block
            {
                Name = "EU Region",
                Type = "Region",
                Region = "EU",
                Size = 400,
                Color = Color.FromArgb(144, 238, 144),
                Details = "Europe Region (400 Parks)"
            }
        };

        // Initialize EU parks data
        var euParks = new List<Block>();
        for (int i = 0; i < 400; i++)
        {
            string parkCode = $"0-ES-BRU-{i:D2}";
            euParks.Add(new Block
            {
                Name = parkCode,
                Type = "Park",
                Region = "EU",
                Size = 1,
                Color = Color.FromArgb(240, 248, 255),
                Details = $"Brussels Park {i:D2}"
            });
        }
        _regions[1].Children = euParks;

        RecalculateBlocks();
    }

    private void InitializeParkControls()
    {
        _controlPanel = new Panel
        {
            Height = 40,
            Dock = DockStyle.Top,
            Padding = new Padding(5)
        };

        _backButton = new Button
        {
            Text = "← Back to Regions",
            Width = 120,
            Location = new Point(5, 8)
        };
        _backButton.Click += (s, e) => SwitchToRegionView();

        _searchBox = new TextBox
        {
            Width = 200,
            Location = new Point(135, 8),
            PlaceholderText = "Search parks..."
        };
        _searchBox.TextChanged += (s, e) =>
        {
            _currentFilter = _searchBox.Text;
            UpdateDisplayedParks();
        };

        _groupingCombo = new ComboBox
        {
            Width = 150,
            Location = new Point(345, 8),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _groupingCombo.Items.AddRange(new object[] { "All", "00-19", "20-39", "40-59", "60-79", "80-99" });
        _groupingCombo.SelectedItem = "All";
        _groupingCombo.SelectedIndexChanged += (s, e) =>
        {
            _currentGroup = _groupingCombo.SelectedItem?.ToString() ?? "All";
            UpdateDisplayedParks();
        };

        _controlPanel.Controls.AddRange(new Control[] { _backButton, _searchBox, _groupingCombo });
    }

    private void RecalculateBlocks()
    {
        var blocks = _isInParkView ? _displayedParks : _regions;
        if (blocks.Count == 0) return;

        // Use different padding based on view type
        int currentPadding = _isInParkView ? PARK_PADDING : REGION_PADDING;

        // Get the available width accounting for scroll bar
        int panelWidth = _mainPanel.ClientSize.Width - (SystemInformation.VerticalScrollBarWidth + 5);

        // Calculate how many columns can fit in the panel
        int cols = Math.Max(1, (panelWidth - currentPadding) / (BlockWidth + currentPadding));
        int rows = (int)Math.Ceiling(blocks.Count / (double)cols);

        // Calculate total width needed for all columns
        int totalBlocksWidth = (cols * BlockWidth) + ((cols - 1) * currentPadding);

        // Calculate the left offset to center the blocks
        float startX = (panelWidth - totalBlocksWidth) / 2f;

        // Position each block
        for (int i = 0; i < blocks.Count; i++)
        {
            int row = i / cols;
            int col = i % cols;

            blocks[i].Rectangle = new RectangleF(
                startX + (col * (BlockWidth + currentPadding)),
                currentPadding + (row * (BlockHeight + currentPadding)),
                BlockWidth,
                BlockHeight
            );
        }

        // Calculate total height needed for all blocks
        int totalHeight = currentPadding + (rows * (BlockHeight + currentPadding));

        // Force the scrollbar to appear by setting appropriate minimum size
        _mainPanel.AutoScrollMinSize = new Size(
            totalBlocksWidth + (2 * (int)startX), // Total width including centering margin
            totalHeight + REGION_PADDING          // Total height plus bottom padding
        );

        // Refresh the panel
        _mainPanel.Invalidate();
    }

    private void DrawBlock(Graphics g, Block block)
    {
        var rect = block.Rectangle;

        using var brush = new SolidBrush(block.Color);
        g.FillRectangle(brush, rect);

        // Use appropriate pen thickness based on view type
        var borderPen = block == _hoveredBlock ? _hoverBorderPen : _normalBorderPen;
        if (_isInParkView)
        {
            // Use thinner lines for parks to reduce visual clutter
            borderPen = block == _hoveredBlock ?
                new Pen(Color.White, 2f) :
                new Pen(Color.FromArgb(50, 50, 50), 0.5f);
        }

        g.DrawRectangle(borderPen, Rectangle.Round(rect));

        // Adjust text padding based on view type
        int textPadding = _isInParkView ? PARK_PADDING * 2 : REGION_PADDING;

        var textRect = new RectangleF(
            rect.X + textPadding,
            rect.Y + textPadding,
            rect.Width - (textPadding * 2),
            rect.Height - (textPadding * 2)
        );

        using var textBrush = new SolidBrush(Color.Black);

        g.DrawString(block.Name, _font, textBrush, new RectangleF(
            textRect.X, textRect.Y, textRect.Width, 25
        ), _centerFormat);

        g.DrawString(block.Details, _smallFont, textBrush, new RectangleF(
            textRect.X, textRect.Y + 30, textRect.Width, textRect.Height - 30
        ), _centerFormat);

        // Dispose of any new pens created for park view
        if (_isInParkView && borderPen != _hoverBorderPen && borderPen != _normalBorderPen)
        {
            borderPen.Dispose();
        }
    }

    private void SwitchToParkView(List<Block> parks)
    {
        if (_controlPanel == null)
        {
            InitializeParkControls();
        }

        _isInParkView = true;
        _allParks = parks;
        _displayedParks = new List<Block>(parks); // Create a copy for filtering
        this.Text = "Parks Visualization - Parks";

        if (_controlPanel != null && !Controls.Contains(_controlPanel))
        {
            Controls.Add(_controlPanel);
        }

        RecalculateBlocks();
        _mainPanel.Invalidate();
    }

    private void SwitchToRegionView()
    {
        _isInParkView = false;
        _displayedParks.Clear();
        _allParks.Clear();
        this.Text = "Parks Visualization - Regions";

        if (_controlPanel != null)
        {
            Controls.Remove(_controlPanel);
        }

        RecalculateBlocks();
        _mainPanel.Invalidate();
    }

    private void UpdateDisplayedParks()
    {
        var filteredParks = _allParks;

        if (_currentGroup != "All")
        {
            var range = _currentGroup.Split('-').Select(int.Parse).ToList();
            filteredParks = filteredParks.Where(b =>
            {
                var parkNumber = int.Parse(b.Name.Split('-').Last());
                return parkNumber >= range[0] && parkNumber <= range[1];
            }).ToList();
        }

        if (!string.IsNullOrWhiteSpace(_currentFilter))
        {
            filteredParks = filteredParks.Where(b =>
                b.Name.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        _displayedParks = filteredParks;
        RecalculateBlocks();
        _mainPanel.Invalidate();
    }

    private void MainPanel_Paint(object sender, PaintEventArgs e)
    {
        e.Graphics.TranslateTransform(_mainPanel.AutoScrollPosition.X, _mainPanel.AutoScrollPosition.Y);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var blocks = _isInParkView ? _displayedParks : _regions;

        var visibleRect = new Rectangle(
            -_mainPanel.AutoScrollPosition.X,
            -_mainPanel.AutoScrollPosition.Y,
            _mainPanel.ClientSize.Width,
            _mainPanel.ClientSize.Height
        );
        visibleRect.Inflate(BlockWidth, BlockHeight);

        foreach (var block in blocks)
        {
            Rectangle blockRect = Rectangle.Round(block.Rectangle);
            if (visibleRect.IntersectsWith(blockRect))
            {
                DrawBlock(e.Graphics, block);
            }
        }
    }

    private void MainPanel_MouseMove(object sender, MouseEventArgs e)
    {
        var blocks = _isInParkView ? _displayedParks : _regions;

        Point mousePoint = new Point(
            e.X - _mainPanel.AutoScrollPosition.X,
            e.Y - _mainPanel.AutoScrollPosition.Y
        );

        var previousHovered = _hoveredBlock;
        var newHovered = blocks.FirstOrDefault(b => b.Rectangle.Contains(mousePoint));

        if (newHovered != previousHovered)
        {
            _hoveredBlock = newHovered;

            if (previousHovered != null)
            {
                var rect = Rectangle.Round(previousHovered.Rectangle);
                rect.Inflate(2, 2);
                _mainPanel.Invalidate(rect);
            }
            if (newHovered != null)
            {
                var rect = Rectangle.Round(newHovered.Rectangle);
                rect.Inflate(2, 2);
                _mainPanel.Invalidate(rect);
            }
        }
    }

    private void MainPanel_MouseClick(object sender, MouseEventArgs e)
    {
        if (_isInParkView) return;

        Point mousePoint = new Point(
            e.X - _mainPanel.AutoScrollPosition.X,
            e.Y - _mainPanel.AutoScrollPosition.Y
        );

        var clickedBlock = _regions.FirstOrDefault(b => b.Rectangle.Contains(mousePoint));

        if (clickedBlock?.Children.Any() == true)
        {
            SwitchToParkView(clickedBlock.Children);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _centerFormat.Dispose();
        _smallFont.Dispose();
        _font.Dispose();
        _normalBorderPen.Dispose();
        _hoverBorderPen.Dispose();
    }
}