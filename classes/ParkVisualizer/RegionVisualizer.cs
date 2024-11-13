// RegionVisualizer.cs - Handles the top level (NA/EU) visualization
public class RegionVisualizer : Form
{
    private List<Block> _regions = new();
    private Block? _hoveredBlock;
    private readonly Font _font = new("Segoe UI", 9.5f);
    private readonly Font _smallFont;
    private readonly StringFormat _centerFormat;
    private readonly Pen _normalBorderPen;
    private readonly Pen _hoverBorderPen;
    private BufferedPanel _mainPanel;

    private const int BlockPadding = 10;
    private const int BlockWidth = 220;
    private const int BlockHeight = 100;

    public RegionVisualizer()
    {
        this.DoubleBuffered = true;
        this.WindowState = FormWindowState.Maximized;
        this.Text = "Parks Visualization - Regions";

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

        // Initialize EU parks data but don't visualize yet
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

    private void RecalculateBlocks()
    {
        if (_regions.Count == 0) return;

        int panelWidth = _mainPanel.ClientSize.Width;
        int cols = Math.Max(1, (panelWidth - BlockPadding) / (BlockWidth + BlockPadding));
        int rows = (int)Math.Ceiling(_regions.Count / (double)cols);

        for (int i = 0; i < _regions.Count; i++)
        {
            int row = i / cols;
            int col = i % cols;

            _regions[i].Rectangle = new RectangleF(
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
        e.Graphics.TranslateTransform(_mainPanel.AutoScrollPosition.X, _mainPanel.AutoScrollPosition.Y);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        foreach (var block in _regions)
        {
            DrawBlock(e.Graphics, block);
        }
    }

    private void MainPanel_MouseMove(object sender, MouseEventArgs e)
    {
        Point mousePoint = new Point(
            e.X - _mainPanel.AutoScrollPosition.X,
            e.Y - _mainPanel.AutoScrollPosition.Y
        );

        var previousHovered = _hoveredBlock;
        _hoveredBlock = _regions.FirstOrDefault(b => b.Rectangle.Contains(mousePoint));

        if (_hoveredBlock != previousHovered)
        {
            _mainPanel.Invalidate();
            _mainPanel.Cursor = _hoveredBlock?.Children.Any() == true ?
                Cursors.Hand : Cursors.Default;
        }
    }

    private void MainPanel_MouseClick(object sender, MouseEventArgs e)
    {
        Point mousePoint = new Point(
            e.X - _mainPanel.AutoScrollPosition.X,
            e.Y - _mainPanel.AutoScrollPosition.Y
        );

        var clickedBlock = _regions.FirstOrDefault(b => b.Rectangle.Contains(mousePoint));

        if (clickedBlock?.Children.Any() == true)
        {
            var parksVisualizer = new ParksVisualizerEU(clickedBlock.Children);
            parksVisualizer.Show();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _centerFormat.Dispose();
        _smallFont.Dispose();
        _normalBorderPen.Dispose();
        _hoverBorderPen.Dispose();
    }
}