﻿using System;
using GraphProcessor;
using UnityEngine;

namespace HLVS.Nodes.ActionNodes
{
	[Serializable, NodeMenuItem("Animation/Set Trigger")]
	public class SetTriggerNode : HlvsActionNode
	{
		public override string name => "Set Animation Trigger";
		
		[Input("Target")]
		public Animator target;

		[Input("Trigger Name")]
		public string triggerName;

		public override ProcessingStatus Evaluate()
		{
			if (!target)
			{
				Debug.Assert(false, "No Animator was found");
				return ProcessingStatus.Finished;
			}
			
			target.SetTrigger(triggerName);
			
			return ProcessingStatus.Finished;
		}
	}
	
	[Serializable, NodeMenuItem("Animation/Set Bool")]
	public class SetBoolNode : HlvsActionNode
	{
		public override string name => "Set Animator Bool";
		
		[Input("Target")]
		public Animator target;

		[Input("Bool Name")]
		public string boolName;
		
		[Input("Value")]
		public bool value;


		public override ProcessingStatus Evaluate()
		{
			if (!target)
			{
				Debug.Assert(false, "No Animator was found");
				return ProcessingStatus.Finished;
			}
			
			target.SetBool(boolName, value);
			
			return ProcessingStatus.Finished;
		}
	}
	
	[Serializable, NodeMenuItem("Animation/Set Float")]
	public class SetFloatNode : HlvsActionNode
	{
		public override string name => "Set Animator Float";
		
		[Input("Target")]
		public Animator target;

		[Input("Float Name")]
		public string floatName;
		
		[Input("Value")]
		public float value;


		public override ProcessingStatus Evaluate()
		{
			if (!target)
			{
				Debug.Assert(false, "No Animator was found");
				return ProcessingStatus.Finished;
			}

			target.SetFloat(floatName, value);
			
			return ProcessingStatus.Finished;
		}
	}
	
	[Serializable, NodeMenuItem("Animation/Set Integer")]
	public class SetIntegerNode : HlvsActionNode
	{
		public override string name => "Set Animator Integer";
		
		[Input("Target")]
		public Animator target;

		[Input("Integer Name")]
		public string floatName;
		
		[Input("Value")]
		public int value;


		public override ProcessingStatus Evaluate()
		{
			if (!target)
			{
				Debug.Assert(false, "No Animator was found");
				return ProcessingStatus.Finished;
			}

			target.SetInteger(floatName, value);
			
			return ProcessingStatus.Finished;
		}
	}
}