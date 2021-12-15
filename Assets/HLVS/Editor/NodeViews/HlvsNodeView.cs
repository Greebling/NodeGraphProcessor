﻿using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GraphProcessor;
using HLVS.Editor.Views;
using HLVS.Nodes;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Direction = UnityEditor.Experimental.GraphView.Direction;

namespace HLVS.Editor.NodeViews
{
	[NodeCustomEditor(typeof(HlvsNode))]
	public class HlvsNodeView : BaseNodeView
	{
		protected override PortView CreatePortView(Direction direction, FieldInfo fieldInfo, PortData portData, BaseEdgeConnectorListener listener)
			=> HlvsPortView.CreatePortView(graph, (HlvsGraphView)owner, this, (HlvsNode)nodeTarget, direction, fieldInfo, portData, listener);

		public HlvsGraph graph => owner.graph as HlvsGraph;

		public override void Enable(bool fromInspector = false)
		{
			base.Enable(fromInspector);

			var nodeStyle = Resources.Load<StyleSheet>("HlvsNodeStyling");
			styleSheets.Add(nodeStyle);

			var portStyle = Resources.Load<StyleSheet>("PortVariableSelector");
			styleSheets.Add(portStyle);
		}

		internal void CheckInputtedData()
		{
			var node = (HlvsNode)nodeTarget;

			foreach (PortView v in inputPortViews)
			{
				var port = (HlvsPortView)v;
				port.TryApplyInputtedValue(node);
			}
			

			ClearErrorMessages();
			var errors = node.CheckFieldInputs();
			if (errors != null && errors.Count != 0)
			{
				GenerateErrorMessages(errors);
			}
		}

		private void GenerateErrorMessages(List<(string fieldName, string errorMessage)> errors)
		{
			List<string> messageTexts = new List<string>();
			foreach ((string fieldName, string errorMessage) error in errors)
			{
				foreach (HlvsPortView port in portsPerFieldName[error.fieldName].Cast<HlvsPortView>())
				{
					var errorLabel = port.portName + ": " + error.errorMessage;
					messageTexts.Add(errorLabel);

					var errorButton = new Button()
					{
						style =
						{
							backgroundColor = new Color(0.85f, 0.2f, 0.17f),
							color = Color.white
						},
						text = "!",
						tooltip = error.errorMessage,
					};
					port.errorBox.Add(errorButton);
				}
			}

			foreach (string messageLabel in messageTexts)
			{
				AddMessageView(messageLabel, EditorGUIUtility.IconContent("CollabConflict").image, new Color(0.75f, 0.11f, 0.21f));
			}
		}

		private void ClearErrorMessages()
		{
			ClearAllBadges();
			foreach (HlvsPortView port in inputPortViews.Cast<HlvsPortView>())
			{
				port.errorBox.Clear();
			}
		}
	}
}