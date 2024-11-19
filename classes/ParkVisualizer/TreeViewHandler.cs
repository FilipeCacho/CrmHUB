namespace CrmHub.ParkVisualizer
{
    public class TreeViewHandler
    {
        private readonly TreeView treeView;

        public TreeViewHandler(TreeView treeView)
        {
            this.treeView = treeView;
            InitializeTreeView();
        }

        private void InitializeTreeView()
        {
            treeView.ShowLines = true;
            treeView.HideSelection = false;
            treeView.DrawMode = TreeViewDrawMode.OwnerDrawAll;
        }

        public void PopulateTreeView(Dictionary<string, List<ParkInfo>> parkData)
        {
            treeView.BeginUpdate();
            treeView.Nodes.Clear();

            foreach (var country in parkData)
            {
                var countryNode = new TreeNode(country.Key)
                {
                    Tag = "country",
                    ImageIndex = 0
                };

                foreach (var park in country.Value)
                {
                    var parkNode = new TreeNode(park.Name)
                    {
                        Tag = park,
                        ImageIndex = 1
                    };
                    countryNode.Nodes.Add(parkNode);
                }

                treeView.Nodes.Add(countryNode);
            }

            treeView.EndUpdate();
        }

        public void DrawNode(DrawTreeNodeEventArgs e)
        {
            if (e.Node.Level == 0)
            {
                e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(230, 230, 230)), e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Node.Text, new Font(treeView.Font, FontStyle.Bold),
                    e.Bounds, Color.Black, TextFormatFlags.VerticalCenter);
            }
            else
            {
                e.Graphics.FillRectangle(Brushes.White, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Node.Text, treeView.Font, e.Bounds,
                    Color.DarkGreen, TextFormatFlags.VerticalCenter);
            }
        }

        public void SearchNodes(string searchText)
        {
            SearchNodesRecursive(treeView.Nodes, searchText.ToLower());
        }

        private void SearchNodesRecursive(TreeNodeCollection nodes, string searchText)
        {
            foreach (TreeNode node in nodes)
            {
                node.BackColor = node.Text.ToLower().Contains(searchText)
                    ? Color.Yellow
                    : Color.White;

                if (node.Nodes.Count > 0)
                    SearchNodesRecursive(node.Nodes, searchText);
            }
        }
    }
}