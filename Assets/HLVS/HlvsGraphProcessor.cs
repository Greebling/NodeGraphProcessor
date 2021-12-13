using System;
using System.Collections.Generic;
using System.Linq;
using GraphProcessor;
using HLVS.Nodes;
using HLVS.Runtime;
using UnityEditor;
using UnityEngine;

namespace HLVS
{
	public class HlvsGraphProcessor<TExecutionStartNode> where TExecutionStartNode : HlvsNode
	{
		List<HlvsNode> _startNodes;

		private readonly HlvsGraph _graph;

		public ProcessingStatus status { get; private set; } = ProcessingStatus.Finished;

		public HlvsGraphProcessor(HlvsGraph graph)
		{
			_graph = graph;
		}

		public void UpdateComputeOrder()
		{
			_startNodes = _graph.nodes.Where(node => node is TExecutionStartNode).Cast<HlvsNode>().ToList();
			if (_startNodes.Count == 0)
				return;

			_startNodes.Sort((node1, node2) => Mathf.CeilToInt((node1.position.y - node2.position.y) * 1024));
			List<HlvsNode> computedNodes = new List<HlvsNode>();

			// First iteration working only on nodes with ExecutionLinks
			{
				HashSet<HlvsNode> alreadyComputedNodes = new HashSet<HlvsNode>();
				int executionOrder = -1;

				foreach (var startNode in _startNodes)
				{
					List<HlvsNode> currNodes = new List<HlvsNode> { startNode };
					List<HlvsNode> nextNodes = new List<HlvsNode>();
					while (currNodes.Count != 0)
					{
						// stops executing infinite loops
						if (executionOrder > 5000)
						{
							var assetPath = AssetDatabase.GetAssetPath(_graph);
							Debug.LogError("Infinite loop in nodes of graph " + assetPath.Split('/').Last() + " detected", _graph);
							break;
						}

						nextNodes.Clear();

						executionOrder++;
						foreach (var node in currNodes)
						{
							if (node.computeOrder < executionOrder)
							{
								node.computeOrder = executionOrder;

								var currNextNodes = GetPossibleNextNodes(node);
								nextNodes.AddRange(currNextNodes);

								if (!alreadyComputedNodes.Contains(node))
								{
									alreadyComputedNodes.Add(node);
									computedNodes.Add(node);
								}
							}
						}

						// swap current with next
						(currNodes, nextNodes) = (nextNodes, currNodes);
					}
				}
			}

			// second iteration taking care of data dependencies
			if (computedNodes.Count != 0)
			{
				int computeOrderOffset = 0;
				var finalNodes = computedNodes.OrderBy(node => node.computeOrder).ToList();

				for (var lowIndex = 0; lowIndex < finalNodes.Count; lowIndex++)
				{
					int currComputeOrder = finalNodes[lowIndex].computeOrder;
					
					// find out all of the nodes processed at the same time
					int highIndex = finalNodes.Count;
					for (int i = lowIndex; i < finalNodes.Count; i++)
					{
						if (finalNodes[i].computeOrder != currComputeOrder)
						{
							highIndex = i;
							break;
						}
					}

					// set nodes compute order and get longest dependency chain length
					currComputeOrder += computeOrderOffset;
					int longestDependency = 0;
					for (int i = lowIndex; i < highIndex; i++)
					{
						var node = finalNodes[i];
						
						if (!node.GetDataInputNodes().Any())
							continue;
						
						int calculatedDependency = LongestDependencyChainLength(node);
						longestDependency = Math.Max(longestDependency, calculatedDependency);
					}

					currComputeOrder += longestDependency;
					for (int i = lowIndex; i < highIndex; i++)
					{
						var node = finalNodes[i];
						node.computeOrder = currComputeOrder;
					}
					
					for (int i = lowIndex; i < highIndex; i++)
					{
						var node = finalNodes[i];
						ComputeDependencyOrder(node);
					}

					computeOrderOffset += longestDependency;
					lowIndex = highIndex - 1; // advance to the next frontier
				}
			}
		}

		private static int LongestDependencyChainLength(HlvsNode start)
		{
			List<BaseNode> frontier = new List<BaseNode>();
			List<BaseNode> nextFrontier = new List<BaseNode>();

			frontier.AddRange(start.GetDataInputNodes().OfType<HlvsDataNode>());
			int dependencyChainLength = 0;

			while (frontier.Count != 0)
			{
				dependencyChainLength++;
				nextFrontier.Clear();

				foreach (var node in frontier)
				{
					nextFrontier.AddRange(node.GetInputNodes().OfType<HlvsDataNode>());
				}

				(frontier, nextFrontier) = (nextFrontier, frontier);
			}

			return dependencyChainLength;
		}

		private static void ComputeDependencyOrder(HlvsNode start)
		{
			List<BaseNode> frontier = new List<BaseNode>();
			List<BaseNode> nextFrontier = new List<BaseNode>();

			frontier.AddRange(start.GetDataInputNodes().OfType<HlvsDataNode>());
			int computeOrder = start.computeOrder;

			while (frontier.Count != 0)
			{
				computeOrder--;
				nextFrontier.Clear();

				foreach (var node in frontier)
				{
					node.computeOrder = computeOrder;

					nextFrontier.AddRange(node.GetInputNodes().OfType<HlvsDataNode>());
				}

				(frontier, nextFrontier) = (nextFrontier, frontier);
			}
		}

		private static (HashSet<HlvsNode> branchBegins, HashSet<HlvsNode> branchEnds) FindBranches(List<HlvsNode> nodes)
		{
			HashSet<HlvsNode> branchBegins = new HashSet<HlvsNode>();
			HashSet<HlvsNode> branchEnds = new HashSet<HlvsNode>();

			foreach (var node in nodes)
			{
				foreach (var inputPort in node.inputPorts)
				{
					if (inputPort.fieldInfo.FieldType == typeof(ExecutionLink) && inputPort.GetEdges().Count > 1)
					{
						branchEnds.Add(node);
					}
				}

				foreach (var outputPort in node.outputPorts)
				{
					if (outputPort.fieldInfo.FieldType == typeof(ExecutionLink) && outputPort.GetEdges().Count > 1)
					{
						branchBegins.Add(node);
					}
				}
			}

			return (branchBegins, branchEnds);
		}


		public void Run()
		{
			if (_startNodes == null)
				UpdateComputeOrder();

			status = ProcessingStatus.Finished;
			List<HlvsNode> nextStartNode = new List<HlvsNode>();
			foreach (var startNode in _startNodes)
			{
				var node = ProcessGraph(startNode);

				if (!(node is TExecutionStartNode))
				{
					status = ProcessingStatus.Unfinished;
				}

				nextStartNode.Add(node);
			}

			_startNodes = nextStartNode;
		}

		protected delegate void ApplyFunction(HlvsNode node);

		protected void Apply(HlvsNode beginNode, ApplyFunction func)
		{
			HlvsNode currNode = GetNextNode(beginNode);
			while (currNode != null)
			{
				func(currNode);

				currNode = GetNextNode(currNode);
			}
		}

		/// <summary>
		/// Applies a given function to all nodes, beginning AFTER startNode
		/// </summary>
		/// <param name="beginNode"></param>
		private HlvsNode ProcessGraph(HlvsNode beginNode)
		{
			HlvsNode currNode = beginNode;
			while (currNode != null)
			{
				GetNodeDataDependencies(currNode);

				currNode.OnProcess();
				var result = currNode.Evaluate();

				if (result == ProcessingStatus.Unfinished)
				{
					return currNode;
				}

				currNode = GetNextNode(currNode);
			}

			// we ran fully through the network so we will begin with the start node next time
			return beginNode is TExecutionStartNode ? beginNode : GetStarterNodeOf(beginNode);
		}

		private TExecutionStartNode GetStarterNodeOf(HlvsNode node)
		{
			while (!(node is TExecutionStartNode))
			{
				Debug.Assert(node.GetInputNodes().Count(baseNode => true) != 0);
				node = GetPreviousNode(node);
			}

			return (TExecutionStartNode)node;
		}

		private static void GetNodeDataDependencies(HlvsNode currNode)
		{
			// get own dependencies
			foreach (var node in currNode.GetInputNodes())
			{
				// ignore action flow as input
				if (node is HlvsActionNode || node is ExecutionStarterNode)
					continue;

				// fetch own data dependencies
				foreach (var nodeInputPort in node.GetInputNodes())
				{
					GetNodeDataDependencies(nodeInputPort as HlvsNode);
				}

				node.OnProcess();

				foreach (var outputPort in node.outputPorts)
				{
					outputPort.PushData();
				}
			}
		}

		public static HlvsNode GetNextNode(HlvsNode node)
		{
			var nextLink = node.nextExecutionLink;
			var nextPort = node.outputPorts
				.Where(port => port.fieldInfo.FieldType == typeof(ExecutionLink))
				.FirstOrDefault(port => port.fieldName == nextLink);
			if (nextPort != null)
			{
				return nextPort.GetEdges().FirstOrDefault()?.inputNode as HlvsNode;
			}
			else
			{
				return null;
			}
		}


		public static IEnumerable<HlvsNode> GetPossibleNextNodes(HlvsNode node)
		{
			return node.outputPorts
				.Where(port => port.fieldInfo.FieldType == typeof(ExecutionLink))
				.Select(port => port.GetEdges().FirstOrDefault()).Where(edge => edge != null)
				.Select(edge => edge.inputNode as HlvsNode);
		}

		public static HlvsNode GetPreviousNode(HlvsNode node)
		{
			return node.GetInputNodes().Last() as HlvsNode;
		}
	}
}