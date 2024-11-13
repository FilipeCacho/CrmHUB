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

    //font size
    private readonly Font _regionTitleFont;
    private readonly Font _regionDetailsFont;
    private readonly Font _parkTitleFont;
    private readonly Font _parkDetailsFont;

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

        // Initialize fonts with different sizes
        _regionTitleFont = new Font("Segoe UI", 16f, FontStyle.Bold);  // Larger, bold font for region names
        _regionDetailsFont = new Font("Segoe UI", 12f);                // Larger font for region details
        _parkTitleFont = new Font("Segoe UI", 9.5f);                  // Original size for park names
        _parkDetailsFont = new Font("Segoe UI", 8.5f);                // Original size for park details

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

    public class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);

            // Critical: Enable both scrollbars and ensure they're visible
            this.AutoScroll = true;
            this.VerticalScroll.Enabled = true;
            this.VerticalScroll.Visible = true;

            // Remove any minimum size constraints
            this.AutoScrollMinSize = new Size(0, 0);

            // Handle scroll wheel
            this.MouseWheel += BufferedPanel_MouseWheel;
        }

        private void BufferedPanel_MouseWheel(object sender, MouseEventArgs e)
        {
            // Custom scroll wheel handling
            int numberOfTextLinesToMove = e.Delta * SystemInformation.MouseWheelScrollLines / 120;
            int numberOfPixelsToMove = numberOfTextLinesToMove * 20; // Adjust scroll speed

            int newY = this.VerticalScroll.Value - numberOfPixelsToMove;
            this.VerticalScroll.Value = Math.Max(this.VerticalScroll.Minimum,
                Math.Min(this.VerticalScroll.Maximum, newY));

            this.Invalidate();
        }
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
        },
        new Block
        {
            Name = "VT Region",
            Type = "Region",
            Region = "VT",
            Size = 0,
            Color = Color.FromArgb(255, 182, 193), // Light pink color
            Details = "Virtual Region"
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

        if (_isInParkView)
        {
            // Parks view layout
            int panelWidth = _mainPanel.ClientSize.Width;
            int currentPadding = PARK_PADDING;

            // Calculate available width accounting for scroll bar
            int availableWidth = panelWidth - SystemInformation.VerticalScrollBarWidth - (2 * currentPadding);

            // Calculate columns that fit in the width
            int cols = Math.Max(1, availableWidth / (BlockWidth + currentPadding));

            // Calculate rows needed for all parks
            int rows = (int)Math.Ceiling(blocks.Count / (double)cols);

            // Calculate total width of blocks in a row
            int totalRowWidth = (cols * BlockWidth) + ((cols - 1) * currentPadding);
            float startX = (availableWidth - totalRowWidth) / 2f + currentPadding;

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

            // Calculate total height including all rows and padding
            int totalHeight = (2 * currentPadding) + (rows * (BlockHeight + currentPadding));

            // Ensure minimum content height is greater than view height to enable scrolling
            int minHeight = Math.Max(totalHeight, _mainPanel.ClientSize.Height + 1);

            // Set scroll size - critical for enabling scrolling
            _mainPanel.SuspendLayout();
            _mainPanel.AutoScrollMinSize = new Size(0, minHeight);
            _mainPanel.VerticalScroll.Maximum = minHeight;
            _mainPanel.ResumeLayout();
        }
        else
        {
            // Regions view layout remains the same
            int panelWidth = _mainPanel.ClientSize.Width;
            int panelHeight = _mainPanel.ClientSize.Height;
            int availableWidth = panelWidth - (2 * REGION_PADDING);
            int blockWidth = (availableWidth - (2 * REGION_PADDING)) / 3;
            int blockHeight = panelHeight - (2 * REGION_PADDING);

            for (int i = 0; i < blocks.Count; i++)
            {
                blocks[i].Rectangle = new RectangleF(
                    REGION_PADDING + (i * (blockWidth + REGION_PADDING)),
                    REGION_PADDING,
                    blockWidth,
                    blockHeight
                );
            }

            _mainPanel.AutoScrollMinSize = new Size(0, 0); // Disable scrolling for region view
        }

        _mainPanel.Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_mainPanel != null)
        {
            RecalculateBlocks();
        }
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

        if (_isInParkView)
        {
            // Parks view - use smaller fonts
            g.DrawString(block.Name, _parkTitleFont, textBrush, new RectangleF(
                textRect.X, textRect.Y, textRect.Width, 25
            ), _centerFormat);

            g.DrawString(block.Details, _parkDetailsFont, textBrush, new RectangleF(
                textRect.X, textRect.Y + 30, textRect.Width, textRect.Height - 30
            ), _centerFormat);
        }
        else
        {
            // Regions view - position text higher up
            float titleHeight = 40;
            float verticalSpacing = 10; // Reduced spacing between title and details
            float topMargin = 50;       // Distance from top of block

            // Draw the title in the upper portion
            g.DrawString(block.Name, _regionTitleFont, textBrush, new RectangleF(
                textRect.X,
                textRect.Y + topMargin, // Position closer to top
                textRect.Width,
                titleHeight
            ), _centerFormat);

            // Draw the details right below the title
            g.DrawString(block.Details, _regionDetailsFont, textBrush, new RectangleF(
                textRect.X,
                textRect.Y + topMargin + titleHeight + verticalSpacing,
                textRect.Width,
                titleHeight
            ), _centerFormat);
        }

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

        // Reset state
        _isInParkView = true;
        _hoveredBlock = null;
        _currentFilter = "";
        _currentGroup = "All";

        // Reset control values
        if (_searchBox != null)
            _searchBox.Text = "";
        if (_groupingCombo != null)
            _groupingCombo.SelectedItem = "All";

        // Update parks data
        _allParks = new List<Block>(parks); // Create a new copy
        _displayedParks = new List<Block>(parks); // Create a new copy for filtering

        this.Text = "Parks Visualization - Parks";

        // Ensure control panel is added
        if (_controlPanel != null && !Controls.Contains(_controlPanel))
        {
            Controls.Add(_controlPanel);
            _controlPanel.BringToFront(); // Ensure control panel is visible
        }

        // Reset scroll position
        _mainPanel.AutoScrollPosition = new Point(0, 0);

        RecalculateBlocks();
        _mainPanel.Invalidate();
    }


    private void SwitchToRegionView()
    {
        // Reset the park view state
        _isInParkView = false;
        _displayedParks.Clear();
        _allParks.Clear();
        _hoveredBlock = null; // Reset hovered state
        this.Text = "Parks Visualization - Regions";

        if (_controlPanel != null)
        {
            Controls.Remove(_controlPanel);
        }

        // Reset any filtering
        _currentFilter = "";
        _currentGroup = "All";

        // Clear scroll position
        if (_mainPanel != null)
        {
            _mainPanel.AutoScrollPosition = new Point(0, 0);
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
        if (_isInParkView)
        {
            // Properly handle scroll transform for parks view
            e.Graphics.TranslateTransform(_mainPanel.AutoScrollPosition.X, _mainPanel.AutoScrollPosition.Y);
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var blocks = _isInParkView ? _displayedParks : _regions;
        if (blocks == null || blocks.Count == 0) return;

        // Calculate visible region including scroll position
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
        if (blocks.Count == 0) return;

        // Correct mouse position calculation considering scroll
        Point mousePoint = new Point(
            e.X - _mainPanel.AutoScrollPosition.X,
            e.Y - _mainPanel.AutoScrollPosition.Y
        );

        var previousHovered = _hoveredBlock;
        var newHovered = blocks.FirstOrDefault(b => b.Rectangle.Contains(mousePoint));

        if (newHovered != previousHovered)
        {
            _hoveredBlock = newHovered;
            _mainPanel.Invalidate(); // Invalidate entire panel to ensure proper redraw
        }
    }

    private void MainPanel_MouseClick(object sender, MouseEventArgs e)
    {
        // Calculate mouse position considering scroll
        Point mousePoint = new Point(
            e.X - _mainPanel.AutoScrollPosition.X,
            e.Y - _mainPanel.AutoScrollPosition.Y
        );

        if (_isInParkView)
        {
            // If in park view, clicking anywhere should do nothing
            return;
        }
        else
        {
            // In region view, check if a region was clicked
            var clickedBlock = _regions.FirstOrDefault(b => b.Rectangle.Contains(mousePoint));

            if (clickedBlock?.Children != null && clickedBlock.Children.Any())
            {
                // Reset scroll position before switching views
                _mainPanel.AutoScrollPosition = new Point(0, 0);

                // Switch to park view with the children of the clicked region
                SwitchToParkView(clickedBlock.Children);
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _centerFormat.Dispose();
        _regionTitleFont.Dispose();
        _regionDetailsFont.Dispose();
        _parkTitleFont.Dispose();
        _parkDetailsFont.Dispose();
        _normalBorderPen.Dispose();
        _hoverBorderPen.Dispose();
    }
}