/*
 * 作者：Peter Xiang
 * 联系方式：565067150@qq.com
 * 文档: https://github.com/PxGame
 * 创建时间: 2019/11/5 14:32:35
 */

using System;
using System.Collections.Generic;

using UnityEngine;

namespace XMLib.AM
{
    /// <summary>
    /// ActionMachine
    /// </summary>
    public class ActionMachine : IActionMachine
    {
        public int waitFrameCnt { get; set; }
        public int frameIndex { get; protected set; }
        public int globalFrameIndex { get; protected set; }
        public int stateBeginFrameIndex { get; protected set; }

        public string configName { get; protected set; }

        public string stateName
        {
            get => cacheStateName;
            protected set
            {
                cacheStateName = value;
                isCacheSConfigDirty = true;
            }
        }

        private List<ActionNode> globalActionNodes { get; set; }
        private List<ActionNode> actionNodes { get; set; }

        public bool isFrameChanged { get; protected set; }
        public bool isStateChanged { get; protected set; }

        public object controller { get; protected set; }
        public IInputContainer input { get; protected set; }

        public string nextStateName { get; protected set; }
        public int nextStatePriority { get; protected set; }

        protected bool isCacheSConfigDirty;
        protected string cacheStateName;
        protected MachineConfig cacheMConfig;
        protected StateConfig cacheSConfig;

        private Dictionary<int, object> dataDict = new Dictionary<int, object>();

        public MachineConfig GetMachineConfig()
        {
            return cacheMConfig ?? (cacheMConfig = ActionMachineHelper.GetMachineConfig(configName));
        }

        public StateConfig GetStateConfig()
        {
            if (isCacheSConfigDirty || cacheSConfig == null)
            {
                cacheSConfig = ActionMachineHelper.GetStateConfig(configName, stateName);
                isCacheSConfigDirty = false;
            }

            return cacheSConfig;
        }

        public int GetStateFrameIndex()
        {
            StateConfig config = GetStateConfig();
            int interval = frameIndex - stateBeginFrameIndex;
            int frameMax = config.frames.Count;
            if (config.enableLoop && interval + 1 > frameMax)
            {
                interval %= frameMax;
            }
            return interval;
        }

        public FrameConfig GetStateFrame()
        {
            StateConfig config = GetStateConfig();
            if (config == null)
            {
                return null;
            }

            int index = GetStateFrameIndex();
            if (index >= 0 && index < config.frames.Count)
            {
                return config.frames[index];
            }

            return null;
        }

        public int GetStateLoopCnt()
        {
            StateConfig config = GetStateConfig();
            int loopCnt = 0;
            if (!config.enableLoop)
            {
                return loopCnt;
            }

            int interval = frameIndex - stateBeginFrameIndex;
            int frameMax = config.frames.Count;
            if (interval + 1 > frameMax)
            {
                loopCnt = Mathf.FloorToInt(interval / frameMax);
            }

            return loopCnt;
        }

        public void ChangeState(string stateName, int priority = 0)
        {
            if (!string.IsNullOrEmpty(stateName) && priority < nextStatePriority)
            {
                return;
            }

            nextStateName = stateName;
            nextStatePriority = priority;
        }

        public void Initialize(string configName, object controller, IInputContainer input)
        {
            waitFrameCnt = 0;
            frameIndex = -1;
            globalFrameIndex = -1;
            stateBeginFrameIndex = -1;
            nextStateName = null;
            nextStatePriority = 0;

            globalActionNodes = new List<ActionNode>();
            actionNodes = new List<ActionNode>();

            this.configName = configName;
            this.controller = controller;
            this.input = input;

            //初始化全局动作
            MachineConfig mConfig = GetMachineConfig();
            InitializeActions(globalActionNodes, mConfig.globalActions, 0);

            //初始化第一个状态
            stateName = mConfig.firstStateName;
            StateConfig sConfig = GetStateConfig();
            stateBeginFrameIndex = frameIndex + 1;
            isStateChanged = true;
            isFrameChanged = true;
            InitializeActions(actionNodes, sConfig.actions, 0);//初始化新的动作
        }

        public void Destroy()
        {
            DisposeActions(actionNodes);
            DisposeActions(globalActionNodes);
            ClearData();
        }

        public void LogicUpdate(float deltaTime)
        {
            InitializeValue();
            UpdateState();
            UpdateFrame(deltaTime);
        }

        #region data operations

        public object this[int tag]
        {
            get => dataDict.TryGetValue(tag, out object value) ? value : null;
            set => dataDict[tag] = value;
        }

        public void SetData(int tag, object data)
        {
            dataDict[tag] = data;
        }

        public bool RemoveData(int tag)
        {
            return dataDict.Remove(tag);
        }

        public bool GetData<T>(int tag, out T data)
        {
            if (dataDict.TryGetValue(tag, out object result))
            {
                try
                {
                    data = (T)result.ConvertTo(typeof(T));
                }
                catch (Exception)
                {
                    data = default(T);
                    return false;
                }

                return true;
            }

            data = default(T);
            return false;
        }

        public bool GetData(int tag, Type type, out object data)
        {
            if (dataDict.TryGetValue(tag, out object result))
            {
                try
                {
                    data = Convert.ChangeType(result, type);
                }
                catch (Exception)
                {
                    data = null;
                    return false;
                }

                return true;
            }

            data = null;
            return false;
        }

        public T GetData<T>(int tag)
        {
            return GetData<T>(tag, out T data) ? data : default(T);
        }

        public T GetData<T>(int tag, T defaultValue)
        {
            return GetData<T>(tag, out T data) ? data : defaultValue;
        }

        public void ClearData()
        {
            dataDict.Clear();
        }

        #endregion data operations

        #region action operations

        private void InitializeActions(List<ActionNode> target, List<object> actions, int index)
        {
            for (int i = 0, count = actions.Count; i < count; i++)
            {
                AddAction(actions[i]);
            }

            return;

            void AddAction(object action)
            {
                ActionNode actionNode = ActionMachineHelper.CreateActionNode();
                actionNode.actionMachine = this;
                actionNode.config = action;
                actionNode.beginFrameIndex = index;
                actionNode.handler = ActionMachineHelper.GetActionHandler(action.GetType());
                target.Add(actionNode);
            }
        }

        private void DisposeActions(List<ActionNode> target)
        {
            for (int i = 0, count = target.Count; i < count; i++)
            {
                RemoveAction(target[i]);
            }
            target.Clear();

            return;

            void RemoveAction(ActionNode action)
            {
                if (action.isUpdating)
                {
                    action.InvokeExit();
                }
                ActionMachineHelper.RecycleActionNode(action);
            }
        }

        private void UpdateActions(List<ActionNode> target, float deltaTime, int index)
        {
            for (int i = 0, count = target.Count; i < count; i++)
            {
                UpdateAction(target[i]);
            }
            return;

            void UpdateAction(ActionNode action)
            {
                if (action.config is IHoldFrames hold && hold.EnableBeginEnd())
                {
                    if (!hold.EnableLoop() && GetStateLoopCnt() > 0)
                    {//只在第一次循环执行
                        return;
                    }

                    if (hold.GetBeginFrame() == index)
                    {
                        action.InvokeEnter();
                    }

                    action.InvokeUpdate(deltaTime);

                    if (hold.GetEndFrame() == index)
                    {
                        action.InvokeExit();
                    }
                }
                else
                {
                    if (action.updateCnt == 0)
                    {
                        action.InvokeEnter();
                    }

                    action.InvokeUpdate(deltaTime);
                }
            }
        }

        #endregion action operations

        private void InitializeValue()
        {
            isFrameChanged = false;
            isStateChanged = globalFrameIndex < 0;//初始状态是改变
        }

        private void UpdateFrame(float deltaTime)
        {
            StateConfig sConfig = GetStateConfig();
            if (null == sConfig)
            {
                throw new RuntimeException("没有状态配置");
            }

            //全局帧，不受顿帧影响
            globalFrameIndex++;

            //更新全局动作-不被顿帧影响
            UpdateActions(globalActionNodes, deltaTime, globalFrameIndex);

            if (waitFrameCnt > 0)
            { //顿帧
                waitFrameCnt--;
                return;
            }

            //帧增加
            frameIndex++;

            int index = GetStateFrameIndex();

            int maxFrameCnt = sConfig.frames.Count;
            if (index >= maxFrameCnt)
            {
                throw new RuntimeException($"当前状态 {sConfig.stateName} 帧序号 {index} 超过上限 {sConfig.frames.Count}");
            }

            //更新动作
            UpdateActions(actionNodes, deltaTime, index);

            //更新事件
            isFrameChanged = true;

            if (!sConfig.enableLoop && index + 1 == maxFrameCnt && string.IsNullOrEmpty(nextStateName))
            { //最后一帧
                ChangeState(sConfig.nextStateName);
            }
        }

        private void UpdateState()
        {
            if (string.IsNullOrEmpty(nextStateName))
            {
                return;
            }

            stateName = nextStateName;//设置新的状态
            stateBeginFrameIndex = frameIndex + 1;//状态起始帧

            StateConfig sConfig = GetStateConfig();
            //释放已有动作
            DisposeActions(actionNodes);
            //初始化新的动作
            InitializeActions(actionNodes, sConfig.actions, frameIndex);

            nextStateName = null;
            nextStatePriority = 0;

            //状态改变
            isStateChanged = true;
        }
    }
}