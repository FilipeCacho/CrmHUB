import { useState, useEffect } from 'react';
import { Folder, Wind, ChevronDown, ChevronRight } from 'lucide-react';

const TreeView = () => {
  const [treeData, setTreeData] = useState([]);
  const [selectedNode, setSelectedNode] = useState(null);
  const [collapsedNodes, setCollapsedNodes] = useState(new Set());
  
  useEffect(() => {
    fetch('/api/tree')
      .then(response => response.json())
      .then(data => {
        setTreeData(data);
        // Initialize all folder nodes as collapsed
        const folderNodes = new Set();
        const initializeCollapsed = (nodes) => {
          nodes.forEach(node => {
            if (node.children?.length > 0) {
              folderNodes.add(node.id);
              initializeCollapsed(node.children);
            }
          });
        };
        initializeCollapsed(data);
        setCollapsedNodes(folderNodes);
      })
      .catch(error => console.error('Error fetching tree data:', error));
  }, []);

  // New helper function to toggle node collapse
  const toggleNodeCollapse = (nodeId, e) => {
    e.stopPropagation();
    setCollapsedNodes(prev => {
      const newCollapsed = new Set(prev);
      if (newCollapsed.has(nodeId)) {
        newCollapsed.delete(nodeId);
      } else {
        newCollapsed.add(nodeId);
      }
      return newCollapsed;
    });
  };

  const TreeNode = ({ node, level = 0 }) => {
    // Node is expanded by default unless explicitly collapsed
    const isCollapsed = collapsedNodes.has(node.id);
    
    return (
      <div>
        <div 
          className="flex items-center py-2 px-2 hover:bg-gray-100 cursor-pointer"
          style={{ 
            paddingLeft: `${level * 20}px`,
            backgroundColor: selectedNode?.id === node.id ? '#e3f2fd' : 'transparent'
          }}
          onClick={() => setSelectedNode(node)}
        >
          {node.children?.length > 0 && (
            <button 
              onClick={(e) => toggleNodeCollapse(node.id, e)}
              className="mr-1"
            >
              {!isCollapsed ? 
                <ChevronDown className="w-4 h-4" /> : 
                <ChevronRight className="w-4 h-4" />
              }
            </button>
          )}
          {node.type === 'folder' ? (
            <Folder className="w-4 h-4 mr-2 text-yellow-500" />
          ) : (
            <Wind className="w-4 h-4 mr-2 text-blue-500" />
          )}
          <span>{node.name}</span>
        </div>
        {!isCollapsed && node.children?.map((child) => (
          <TreeNode key={child.id} node={child} level={level + 1} />
        ))}
      </div>
    );
  };

  const DetailPane = ({ node }) => {
    if (!node) return (
      <div className="p-4 text-gray-500">
        Select an item to view details
      </div>
    );

    return (
      <div className="p-4">
        <h2 className="text-xl font-bold mb-4">{node.name}</h2>
        <div className="space-y-2">
          <p><span className="font-semibold">Type:</span> {node.type}</p>
          <p><span className="font-semibold">ID:</span> {node.id}</p>
          {node.description && (
            <p><span className="font-semibold">Description:</span> {node.description}</p>
          )}
        </div>
      </div>
    );
  };

  return (
    <div className="flex h-screen w-full">
      <div className="w-64 border-r overflow-y-auto bg-white">
        <div className="p-4 border-b font-semibold bg-gray-50">
          Park Explorer
        </div>
        {treeData.map((node) => (
          <TreeNode key={node.id} node={node} />
        ))}
      </div>
      <div className="flex-1 overflow-y-auto bg-white">
        <DetailPane node={selectedNode} />
      </div>
    </div>
  );
};

export default TreeView;