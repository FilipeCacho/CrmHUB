using Label = System.Windows.Forms.Label;

namespace CrmHub.ParkVisualizer
{
    public class ParkVisualizerForm : Form
    {
        private TreeView treeView;
        private TextBox searchBox;
        private Panel sidebarPanel;
        private Panel detailPanel;
        private TreeViewHandler treeViewHandler;

        public ParkVisualizerForm()
        {
            InitializeComponents();
            LoadDemoData();
        }

        private void InitializeComponents()
        {
            this.Size = new Size(1200, 800);
            this.Text = "Parks Management System (Demo)";

            InitializePanels();
            InitializeSearchBox();
            InitializeTreeView();

            this.Controls.Add(detailPanel);
            this.Controls.Add(treeView);
            this.Controls.Add(sidebarPanel);
            sidebarPanel.Controls.Add(searchBox);
        }

        private void InitializePanels()
        {
            sidebarPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 250,
                BackColor = Color.FromArgb(240, 240, 240)
            };

            detailPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 300,
                BackColor = Color.White
            };
        }

        private void InitializeSearchBox()
        {
            searchBox = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 30,
                PlaceholderText = "Search parks..."
            };
            searchBox.TextChanged += SearchBox_TextChanged;
        }

        private void InitializeTreeView()
        {
            treeView = new TreeView { Dock = DockStyle.Fill };
            treeViewHandler = new TreeViewHandler(treeView);
            treeView.DrawNode += TreeView_DrawNode;
            treeView.AfterSelect += TreeView_AfterSelect;
        }

        private void LoadDemoData()
        {
            var demoData = DemoData.GetDemoParks();
            treeViewHandler.PopulateTreeView(demoData);
        }

        private void TreeView_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            treeViewHandler.DrawNode(e);
        }

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            treeViewHandler.SearchNodes(searchBox.Text);
        }

        private void TreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag is ParkInfo parkInfo)
            {
                UpdateDetailPanel(parkInfo);
            }
        }

        private void UpdateDetailPanel(ParkInfo parkInfo)
        {
            detailPanel.Controls.Clear();

            var detailsLabel = new Label
            {
                AutoSize = true,
                Location = new Point(10, 10),
                Text = $"Name: {parkInfo.Name}\n\n" +
                       $"Location: {parkInfo.Location}\n\n" +
                       $"Size: {parkInfo.Size:N0} sq km\n\n" +
                       $"Description: {parkInfo.Description}"
            };

            detailPanel.Controls.Add(detailsLabel);
        }
    }
}