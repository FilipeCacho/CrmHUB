// handles all displaying and interaction with the parks with in a region
public class ParksVisualizerEU : Form
{
    private List<Block> _allParks;
    private List<Block> _displayedParks;
    private Block? _hoveredBlock;
    private readonly Font _font = new("Segoe UI", 9.5f);
    private readonly Font _smallFont;
    private readonly StringFormat _centerFormat;
    private readonly Pen _normalBorderPen;
    private readonly Pen _hoverBorderPen;
    private string _currentFilter = "";
    private string _currentGroup = "All";

    private BufferedPanel _mainPanel;
    private ComboBox _groupingCombo;
    private TextBox _searchBox;

    private const int BlockPadding = 10;
    private const int BlockWidth = 220;
    private const int BlockHeight = 100;

    public ParksVisualizerEU(List<Block> parks)
    {
        _allParks = parks;
        _displayedParks = parks;

        this.DoubleBuffered = true;
        this.WindowState = FormWindowState.Maximized;
        this.Text = "Parks Visualization - Parks";

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
        UpdateDisplayedParks();
    }

    private void InitializeControls()
    {
        // Control panel
        var controlPanel = new Panel
        {
            Height = 40,
            Dock = DockStyle.Top,
            Padding = new Padding(5)
        };

        _searchBox = new TextBox
        {
            Width = 200,
            Location = new Point(5, 8),
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
            Location = new Point(215, 8),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _groupingCombo.Items.AddRange(new object[] { "All", "00-19", "20-39", "40-59", "60-79", "80-99" });
        _groupingCombo.SelectedItem = "All";
        _groupingCombo.SelectedIndexChanged += (s, e) =>
        {
            _currentGroup = _groupingCombo.SelectedItem.ToString();
            UpdateDisplayedParks();
        };

        controlPanel.Controls.AddRange(new Control[] { _searchBox, _groupingCombo });

        // Main panel
        _mainPanel = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.White
        };
        _mainPanel.Paint += MainPanel_Paint;
        _mainPanel.MouseMove += MainPanel_MouseMove;
        _mainPanel.Resize += (s, e) => RecalculateBlocks();

        // Add controls
        this.Controls.Add(_mainPanel);
        this.Controls.Add(controlPanel);
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

    private void RecalculateBlocks()
    {
        if (_displayedParks.Count == 0) return;

        int panelWidth = _mainPanel.ClientSize.Width;
        int cols = Math.Max(1, (panelWidth - BlockPadding) / (BlockWidth + BlockPadding));
        int rows = (int)Math.Ceiling(_displayedParks.Count / (double)cols);

        for (int i = 0; i < _displayedParks.Count; i++)
        {
            int row = i / cols;
            int col = i % cols;

            _displayedParks[i].Rectangle = new RectangleF(
                BlockPadding + (col * (BlockWidth + BlockPadding)),
                BlockPadding + (row * (BlockHeight + BlockPadding)),
                BlockWidth,
                BlockHeight
            );
        }

        int totalHeight = BlockPadding + (rows * (BlockHeight + BlockPadding));
        _mainPanel.AutoScrollMinSize = new Size(
            panelWidth,
            Math.Max(totalHeight, _mainPanel.ClientSize.Height)
        );
    }

    private void DrawBlock(Graphics g, Block block)
    {
        var rect = block.Rectangle;

        using var brush = new SolidBrush(block.Color);
        g.FillRectangle(brush, rect);

        g.DrawRectangle(
            block == _hoveredBlock ? _hoverBorderPen : _normalBorderPen,
            Rectangle.Round(rect)
        );

        var textRect = new RectangleF(
            rect.X + BlockPadding,
            rect.Y + BlockPadding,
            rect.Width - (BlockPadding * 2),
            rect.Height - (BlockPadding * 2)
        );

        using var textBrush = new SolidBrush(Color.Black);

        g.DrawString(block.Name, _font, textBrush, new RectangleF(
            textRect.X, textRect.Y, textRect.Width, 25
        ), _centerFormat);

        g.DrawString(block.Details, _smallFont, textBrush, new RectangleF(
            textRect.X, textRect.Y + 30, textRect.Width, textRect.Height - 30
        ), _centerFormat);
    }

    private void MainPanel_Paint(object sender, PaintEventArgs e)
    {
        if (_displayedParks.Count == 0) return;

        e.Graphics.TranslateTransform(_mainPanel.AutoScrollPosition.X, _mainPanel.AutoScrollPosition.Y);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var visibleRect = new Rectangle(
            -_mainPanel.AutoScrollPosition.X,
            -_mainPanel.AutoScrollPosition.Y,
            _mainPanel.ClientSize.Width,
            _mainPanel.ClientSize.Height
        );
        visibleRect.Inflate(BlockWidth, BlockHeight);

        foreach (var block in _displayedParks)
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
        if (_displayedParks.Count == 0) return;

        Point mousePoint = new Point(
            e.X - _mainPanel.AutoScrollPosition.X,
            e.Y - _mainPanel.AutoScrollPosition.Y
        );

        var previousHovered = _hoveredBlock;
        var newHovered = _displayedParks.FirstOrDefault(b => b.Rectangle.Contains(mousePoint));

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