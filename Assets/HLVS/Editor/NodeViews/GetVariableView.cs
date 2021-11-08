using System;
using GraphProcessor;
using HLVS.Nodes;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HLVS.Editor.NodeViews
{
	[NodeCustomEditor(typeof(GetVariableNode))]
	public class GetVariableView : HlvsNodeView
	{
		private GetVariableNode target => nodeTarget as GetVariableNode;

		private TextField _nameField;
		private Label _titleLabel;

		private ExposedParameter _parameter;

		public override void Enable()
		{
			_titleLabel = titleContainer.Q<Label>("title-label");

			_nameField = new TextField("Name");
			_nameField.value = target.variableName;
			_nameField.Q<Label>().style.minWidth = 10;
			_nameField.Q("unity-text-input").style.minWidth = 75;
			_nameField.RegisterCallback<FocusOutEvent>(e => OnNameChanged());
			inputContainer.Add(_nameField);
		}

		private void UpdateVisuals()
		{
			if (_parameter == null)
			{
				Debug.LogError($"No variable called '{target.variableName}' found");
			}
			else
			{
				(outputPortViews[0] as HlvsPortView).SetPortType(_parameter.GetValueType());
				RefreshPorts();
			}
		}

		private void OnNameChanged()
		{
			Undo.RecordObject(owner.graph, "Set GetVariable node");
			
			target.variableName = _nameField.value;
			
			if(target.variableName != String.Empty)
			{
				_titleLabel.text = target.name;
				_parameter = graph.GetVariable(target.variableName);
				UpdateVisuals();
			}
			
			EditorUtility.SetDirty(owner.graph);
		}
	}
}