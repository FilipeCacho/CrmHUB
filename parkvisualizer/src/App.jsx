import { useState, useEffect } from 'react';
import { Folder, Wind, ChevronDown, ChevronRight } from 'lucide-react';
import usFlag from './assets/flags/us.svg';
import euFlag from './assets/flags/european_union.svg';

// Icon mapping for custom node icons
const customIcons = {
  "1": usFlag,    // Wind Park Alpha ID
  "5": euFlag     // Turbine Section B ID
};

const TreeView = () => {
  const [treeData, setTreeData] = useState([]);
  const [selectedNode, setSelectedNode] = useState(null);
  const [collapsedNodes, setCollapsedNodes] = useState(new Set());
  
  // Reduced sizes for section headers and icons
  const sectionStyles = {
    icon: "w-8 h-8 mr-2", // Reduced from w-12 h-12
    text: "text-lg font-semibold", // Reduced from text-xl
    container: "mb-3 pb-2" // Reduced spacing
  };

  useEffect(() => {
    fetch('/api/tree')
      .then(response => response.json())
      .then(data => {
        // Restructure data to put European section at the same level
        const restructuredData = [];
        
        data.forEach(node => {
          if (node.id === "1") { // Wind Park Alpha
            // Add main node without Turbine Section B
            const modifiedNode = {
              ...node,
              children: node.children.filter(child => child.id !== "5") // Remove Turbine Section B
            };
            restructuredData.push(modifiedNode);
          }
          
          // Add Turbine Section B as a top-level node
          const sectionB = node.children?.find(child => child.id === "5");
          if (sectionB) {
            restructuredData.push(sectionB);
          }
        });

        setTreeData(restructuredData);
        
        // Initialize collapsed nodes
        const folderNodes = new Set();
        const initializeCollapsed = (nodes) => {
          nodes.forEach(node => {
            if (node.children?.length > 0) {
              folderNodes.add(node.id);
              initializeCollapsed(node.children);
            }
          });
        };
        initializeCollapsed(restructuredData);
        setCollapsedNodes(folderNodes);
      })
      .catch(error => console.error('Error fetching tree data:', error));
  }, []);

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

  const TreeNode = ({ node, level = 0, isTopLevel = false }) => {
    const isCollapsed = collapsedNodes.has(node.id);
    
    const renderIcon = () => {
      if (customIcons[node.id]) {
        return (
          <img 
            src={customIcons[node.id]} 
            className={isTopLevel ? sectionStyles.icon : "w-4 h-4 mr-2"}
            alt={`Icon for ${node.name}`}
          />
        );
      }
      
      return node.type === 'folder' ? (
        <Folder className={isTopLevel ? sectionStyles.icon : "w-4 h-4 mr-2 text-yellow-500"} />
      ) : (
        <Wind className="w-4 h-4 mr-2 text-blue-500" />
      );
    };

    if (isTopLevel) {
      return (
        <div className={sectionStyles.container}>
          <div 
            className="flex items-center py-2 px-2 hover:bg-gray-100 cursor-pointer"
            onClick={() => setSelectedNode(node)}
          >
            {node.children?.length > 0 && (
              <button 
                onClick={(e) => toggleNodeCollapse(node.id, e)}
                className="mr-1"
              >
                {!isCollapsed ? 
                  <ChevronDown className="w-5 h-5" /> : 
                  <ChevronRight className="w-5 h-5" />
                }
              </button>
            )}
            {renderIcon()}
            <span className={sectionStyles.text}>{node.name}</span>
          </div>
          {!isCollapsed && node.children?.map((child) => (
            <TreeNode key={child.id} node={child} level={1} />
          ))}
        </div>
      );
    }

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
          {renderIcon()}
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
      <div className="w-72 border-r overflow-y-auto bg-white">
        <div className="p-4 border-b font-semibold bg-gray-50">
          Park Explorer
        </div>
        {treeData.map((node) => (
          <TreeNode key={node.id} node={node} isTopLevel={true} />
        ))}
      </div>
      <div className="flex-1 overflow-y-auto bg-white">
        <DetailPane node={selectedNode} />
      </div>
    </div>
  );
};

export default TreeView;