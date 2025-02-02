﻿using System;
using System.Collections.Generic;
using System.Linq;
using GraphProcessor;
using HLVS.Nodes;
using HLVS.Runtime;
using IkTools.FormulaParser;
using UnityEngine;

namespace HLVS
{
	public class HlvsGraph : BaseGraph, IVariableProvider<HlvsGraph>, INodeEventListener
	{
		private HlvsGraphProcessor _processor;

		public List<HlvsBlackboard> blackboards;

		/// <summary>
		/// Blueprint of which types are needed as parameters
		/// </summary>
		[SerializeReference]
		public List<ExposedParameter> parametersBlueprint = new();

		[NonSerialized]
		public GameObject activeGameObject;

		public Action                 onParameterListChanged  = () => { };
		public Action                 onBlackboardListChanged = () => { };
		public Action<HlvsBlackboard> onBlackboardAdded       = (HlvsBlackboard) => { };
		public Action<HlvsBlackboard> onBlackboardRemoved     = (HlvsBlackboard) => { };

		private readonly Dictionary<string, ExposedParameter> _upperCaseNameToVar = new();
		private readonly Dictionary<string, ExposedParameter> _guidToVar          = new();

		public readonly Dictionary<string, int> nodeGuidToIndex = new ();

		private readonly Dictionary<string, List<OnEventNode>>     _eventNodes = new();
		private readonly Dictionary<string, List<OnZoneEventNode>> _zoneNodes  = new();

		private bool _hasCollisionNodes;

		public virtual void OnCreated()
		{
			var self = new GameObjectParameter();
			self.Initialize("Self", null);
			parametersBlueprint.Add(self);
			onParameterListChanged();
		}

		protected override void OnEnable()
		{
			blackboards = blackboards == null ? new List<HlvsBlackboard>() : blackboards.Where(blackboard => blackboard).ToList();

			BuildNodeDict();
			onGraphChanges += changes =>
			{
				if (changes.addedNode != null)
				{
					nodeGuidToIndex.Add(changes.addedNode.GUID, nodes.Count - 1);
				}
			};

			onGraphChanges += changes =>
			{
				if (changes.removedNode != null)
				{
					nodeGuidToIndex.Clear();
					BuildNodeDict();
				}
			};

			BuildVariableDict();
			onParameterListChanged += BuildVariableDict;
			onBlackboardListChanged += BuildVariableDict;
			base.OnEnable();

			foreach (var baseNode in nodes)
			{
				var node = (HlvsNode)baseNode;
				node.Graph = this;
			}
		}

		private void OnDestroy()
		{
			foreach (string listenedEvent in _listenedEvents)
			{
				EventManager.instance.RemoveListener(this, listenedEvent);
			}
		}

		public void Init()
		{
			foreach (BaseNode baseNode in nodes)
			{
				var node = (HlvsNode)baseNode;
				node.Reset();
				node.ParseExpressions();
			}
			UpdateComputeOrder();
			
			_hasCollisionNodes = nodes.Any(node => node is OnCollisionNode);
#if UNITY_EDITOR
			if (!activeGameObject || !Application.isPlaying)
				return;

			ScanEventNodes();
			ScanZoneNodes();
#endif
		}

		public override void UpdateComputeOrder()
		{
			foreach (var baseNode in nodes)
			{
				baseNode.computeOrder = -1;
			}

			if (_processor == null)
			{
				_processor = new HlvsGraphProcessor(this);
				_processor.RegisterType(typeof(OnUpdateNode));
				_processor.RegisterType(typeof(OnStartNode));
				_processor.RegisterType(typeof(OnTriggerEnteredNode));
				_processor.RegisterType(typeof(OnCollisionNode));
			}

			_processor.UpdateComputeOrder();
		}

		public void RunStartNodes()
		{
			_processor.Run(typeof(OnStartNode));
		}

		public void RunUpdateNodes()
		{
			_processor.RunAllPausedNodes();
			_processor.Run(typeof(OnUpdateNode));
		}

		public void RunOnTriggerEnteredNodes(GameObject enteredObject)
		{
			foreach (BaseNode baseNode in nodes)
			{
				if (baseNode is OnTriggerEnteredNode node)
				{
					node.enteredObject = enteredObject;
				}
			}

			_processor.Run(typeof(OnTriggerEnteredNode));
		}

		public void RunOnTriggerExitNodes(GameObject enteredObject)
		{
			foreach (BaseNode baseNode in nodes)
			{
				if (baseNode is OnTriggerExitNode node)
				{
					node.exitingObject = enteredObject;
				}
			}

			_processor.Run(typeof(OnTriggerExitNode));
		}

		public void RunOnCollisionEnteredNodes(GameObject enteredObject)
		{
			if (!_hasCollisionNodes)
				return;

			foreach (BaseNode baseNode in nodes)
			{
				if (baseNode is OnCollisionNode node)
				{
					node.enteredObject = enteredObject;
				}
			}

			_processor.Run(typeof(OnCollisionNode));
		}

		public void AddBlackboard(HlvsBlackboard board)
		{
			if (blackboards.Contains(board))
			{
				Debug.LogError("This graph already contains this blackboard");
				return;
			}


			// check for duplicate variables
			{
				HashSet<string> currentVariableNames = new HashSet<string>();
				foreach (var param in blackboards.SelectMany(blackboard => blackboard.fields))
				{
					currentVariableNames.Add(param.name.ToUpperInvariant());
				}

				foreach (var param in parametersBlueprint)
				{
					currentVariableNames.Add(param.name.ToUpperInvariant());
				}

				bool canAddBoard = true;
				foreach (ExposedParameter exposedParameter in board.fields)
				{
					var currVar = exposedParameter.name.ToUpperInvariant();
					if (currentVariableNames.Contains(currVar))
					{
						Debug.LogError($"This graph already contains a parameter or a blackboard with a variable similar to '{exposedParameter.name}'");
						canAddBoard = false;
					}
				}

				if (!canAddBoard)
					return;
			}

			blackboards.Add(board);
			onBlackboardAdded(board);
		}

		public void RemoveBlackboard(HlvsBlackboard board)
		{
			Debug.Assert(blackboards.Contains(board));
			blackboards.Remove(board);
			onBlackboardRemoved(board);
		}

		public IEnumerable<ExposedParameter> GetParameters()
		{
			return parametersBlueprint.OrderBy(parameter => parameter.name);
		}

		public IEnumerable<ExposedParameter> GetBlackboardFields()
		{
			return blackboards.SelectMany(blackboard => blackboard.fields).OrderBy(parameter => parameter.name);
		}

		public ExposedParameter GetVariableByName(string variableName)
		{
			return GetVariableByUppercaseName(variableName.ToUpper().Replace(' ', '_'));
		}

		public ExposedParameter GetVariableByUppercaseName(string variableName)
		{
			if (_upperCaseNameToVar.TryGetValue(variableName, out ExposedParameter val))
			{
				return val;
			}

			return null;
		}

		public ExposedParameter GetVariableByGuid(string guid)
		{
			if (_guidToVar.TryGetValue(guid, out ExposedParameter val))
			{
				return val;
			}
			
			return null;
		}

		private void BuildNodeDict()
		{
			for (int i = 0; i < nodes.Count; i++)
			{
				nodeGuidToIndex.Add(nodes[i].GUID, i);
			}
		}

		private void BuildVariableDict()
		{
			_upperCaseNameToVar.Clear();
			blackboards.ForEach(blackboard =>
				blackboard.RuntimeInstance.fields.ForEach(parameter => _upperCaseNameToVar.Add(parameter.name.ToUpperInvariant().Replace(' ', '_'), parameter)));
			parametersBlueprint.ForEach(parameter => _upperCaseNameToVar.Add(parameter.name.ToUpperInvariant().Replace(' ', '_'), parameter));

			_guidToVar.Clear();
			blackboards.ForEach(blackboard => blackboard.RuntimeInstance.fields.ForEach(parameter => _guidToVar.Add(parameter.guid, parameter)));
			parametersBlueprint.ForEach(parameter => _guidToVar.Add(parameter.guid, parameter));
		}

		public void InitParameterValues(GameObject currentGameObject, List<ExposedParameter> parameters)
		{
			Debug.Assert(parameters.Count == parametersBlueprint.Count, "Parameter lists don't match");

			for (int i = 0; i < parametersBlueprint.Count; i++)
			{
				if (parametersBlueprint[i].name == "Self")
					parametersBlueprint[i].value = currentGameObject;
				else
					parametersBlueprint[i].value = parameters[i].value;
			}
		}

		public Func<HlvsGraph, double> Get(string name)
		{
			return graph => Convert.ToDouble(GetFromInternalFunction(name) ?? graph.GetVariableByUppercaseName(name).value);
		}

		public object GetFromInternalFunction(string name)
		{
			switch (name)
			{
				default:
					return null;
				case "TIME":
					return Time.time;
				case "DELTA_TIME":
					return Time.deltaTime;
			}
		}

		private HashSet<string> _listenedEvents = new HashSet<string>();

		private void ScanEventNodes()
		{
			foreach (string listenedEvent in _listenedEvents)
			{
				EventManager.instance.RemoveListener(this, listenedEvent);
			}

			_listenedEvents.Clear();
			_eventNodes.Clear();
			foreach (OnEventNode node in nodes.Where(node => node is OnEventNode).Cast<OnEventNode>().Where(node => node.eventName != string.Empty))
			{
				if (!_listenedEvents.Contains(node.eventName))
				{
					EventManager.instance.RegisterListener(this, node.eventName);
					_listenedEvents.Add(node.eventName);
				}

				List<OnEventNode> eventList;
				if (_eventNodes.TryGetValue(node.eventName, out eventList))
				{
					eventList.Add(node);
				}
				else
				{
					eventList = new List<OnEventNode>() { node };
					_eventNodes.Add(node.eventName, eventList);
				}
			}
		}

		private void ScanZoneNodes()
		{
			_zoneNodes.Clear();
			foreach (OnZoneEventNode node in nodes.Where(node => node is OnZoneEventNode).Cast<OnZoneEventNode>().Where(node => node.zoneName != string.Empty))
			{
				List<OnZoneEventNode> nodeList;
				if (_zoneNodes.TryGetValue(node.zoneName + node.activationType, out nodeList))
				{
					nodeList.Add(node);
				}
				else
				{
					_zoneNodes.Add(node.zoneName + node.activationType, new List<OnZoneEventNode>() { node });
					switch (node.activationType)
					{
						case ZoneNotificationType.Enter:
							ZoneManager.Instance.RegisterOnEnter(node.zoneName, other =>
							{
								var nodes = _zoneNodes[node.zoneName + ZoneNotificationType.Enter];
								foreach (OnZoneEventNode onZoneEventNode in nodes)
								{
									onZoneEventNode.other = other;
								}

								_processor.RunFromNodes(_zoneNodes[node.zoneName + node.activationType], typeof(OnZoneEventNode));
							});
							break;
						case ZoneNotificationType.Stay:
							ZoneManager.Instance.RegisterOnStay(node.zoneName, other =>
							{
								var nodes = _zoneNodes[node.zoneName + ZoneNotificationType.Stay];
								foreach (OnZoneEventNode onZoneEventNode in nodes)
								{
									onZoneEventNode.other = other;
								}

								_processor.RunFromNodes(_zoneNodes[node.zoneName + node.activationType], typeof(OnZoneEventNode));
							});
							break;
						case ZoneNotificationType.Exit:
							ZoneManager.Instance.RegisterOnExit(node.zoneName, other =>
							{
								var nodes = _zoneNodes[node.zoneName + ZoneNotificationType.Exit];
								foreach (OnZoneEventNode onZoneEventNode in nodes)
								{
									onZoneEventNode.other = other;
								}

								_processor.RunFromNodes(_zoneNodes[node.zoneName + node.activationType], typeof(OnZoneEventNode));
							});
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}
				}
			}
		}

		public void OnEvent(HlvsEvent e)
		{
			if (!activeGameObject)
				return;

			foreach (OnEventNode onEventNode in _eventNodes[e.name])
			{
				onEventNode.eventData = e.parameters;
			}

			_processor.RunFromNodes(_eventNodes[e.name].Where(node => node != null), typeof(OnEventNode));
		}
	}
}