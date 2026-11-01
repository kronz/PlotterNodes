// Dynamo 4.4 (D:\Drawing\Dynamo, master) API surface used by PlotterNodes.Canvas: DynamoCore,
// DynamoCoreWpf, DynamoServices, ProtoCore. Accessibility matches the real code, including the
// protected / protected internal members of the Watch3D view models and the internal
// DynamoSelection. Stub bodies only; see ../README.md.
#pragma warning disable CS0067, CS0108, CS0114, CS1591, CS0649
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml;
using Dynamo.Engine;
using Dynamo.Graph.Connectors;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.Logging;
using Dynamo.Models;
using Dynamo.Scheduler;
using Dynamo.Visualization;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.Wpf.SharpDX;
using ProtoCore.AST.AssociativeAST;
using SharpDX;

namespace ProtoCore.AST.AssociativeAST
{
    public abstract class AssociativeNode { }
    public class IdentifierNode : AssociativeNode
    {
        public string Value { get; set; }
    }
    public class NullNode : AssociativeNode { }
    public class IntNode : AssociativeNode { }
    public class StringNode : AssociativeNode { }
    public class BooleanNode : AssociativeNode { }
    public class BinaryExpressionNode : AssociativeNode { }

    public static class AstFactory
    {
        public static NullNode BuildNullNode() { throw null; }
        public static BinaryExpressionNode BuildAssignment(AssociativeNode lhs, AssociativeNode rhs) { throw null; }
        public static IntNode BuildIntNode(int value) { throw null; }
        public static IdentifierNode BuildIdentifier(string name) { throw null; }
        public static StringNode BuildStringNode(string str) { throw null; }
        public static BooleanNode BuildBooleanNode(bool value) { throw null; }
        public static AssociativeNode BuildIndexExpression(AssociativeNode value, AssociativeNode index) { throw null; }
        public static AssociativeNode BuildFunctionCall<T1, T2, T3, T4, TRetType>(Func<T1, T2, T3, T4, TRetType> a, List<AssociativeNode> arguments, object core = null) { throw null; }
    }
}

namespace Dynamo.Core
{
    public abstract class NotificationObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void RaisePropertyChanged(string propertyName) { }
    }
}

namespace Dynamo.Engine
{
    public class EngineController { }
}

namespace Dynamo.Scheduler
{
    public interface IScheduler { }
}

namespace Dynamo.Visualization
{
    public interface IRenderPackageFactory { }

    public class RenderPackageCache
    {
        public bool IsEmpty() { throw null; }
    }
}

namespace Dynamo.Logging
{
    public enum WarningLevel { Mild, Moderate, Error }

    public interface ILogger
    {
        void Log(string message);
        void Log(string tag, string message);
        void LogError(string error);
        void LogWarning(string warning, WarningLevel level);
        void Log(Exception e);
    }

    public class DynamoLogger : Dynamo.Core.NotificationObject, ILogger, IDisposable
    {
        public void Log(string message) { }
        public void Log(string tag, string message) { }
        public void LogError(string error) { }
        public void LogWarning(string warning, WarningLevel level) { }
        public void Log(Exception e) { }
        public void Dispose() { }
    }
}

namespace Dynamo.Graph
{
    public enum SaveContext { [Obsolete("Use Save or SaveAs, instead of File")] File, Copy, Undo, Preset, None, Save, SaveAs };

    public abstract class ModelBase : Dynamo.Core.NotificationObject
    {
        public virtual Guid GUID { get; set; }
        public bool IsSelected { get; set; }
        public virtual void Dispose() { }
        protected abstract void SerializeCore(XmlElement element, SaveContext context);
        protected abstract void DeserializeCore(XmlElement nodeElement, SaveContext context);
    }
}

namespace Dynamo.Graph.Nodes
{
    public enum PortType { Input, Output }
    public enum LacingStrategy { Disabled, First, Shortest, Longest, CrossProduct, Auto }

    public class PortModel : Dynamo.Graph.ModelBase
    {
        public ObservableCollection<ConnectorModel> Connectors { get { throw null; } }
        public PortType PortType { get { throw null; } }
        public NodeModel Owner { get { throw null; } }
        public int Index { get { throw null; } }
        public AssociativeNode DefaultValue { get; set; }
        public bool UsingDefaultValue { get; set; }
        protected override void SerializeCore(XmlElement element, Dynamo.Graph.SaveContext context) { }
        protected override void DeserializeCore(XmlElement nodeElement, Dynamo.Graph.SaveContext context) { }
    }

    public abstract class NodeModel : Dynamo.Graph.ModelBase
    {
        protected NodeModel() { }
        protected NodeModel(IEnumerable<PortModel> inPorts, IEnumerable<PortModel> outPorts) { }
        public string Name { get; set; }
        public ObservableCollection<PortModel> InPorts { get { throw null; } }
        public ObservableCollection<PortModel> OutPorts { get { throw null; } }
        public LacingStrategy ArgumentLacing { get; set; }
        protected bool ShouldDisplayPreviewCore { get; set; }
        public void RegisterAllPorts() { }
        public virtual IEnumerable<AssociativeNode> BuildOutputAst(List<AssociativeNode> inputAstNodes) { throw null; }
        public virtual IdentifierNode GetAstIdentifierForOutputIndex(int outputIndex) { throw null; }
        public IdentifierNode AstIdentifierForPreview { get { throw null; } }
        public string AstIdentifierBase { get { throw null; } }
        public virtual void OnNodeModified(bool forceExecute = false) { }
        public void MarkNodeAsModified(bool forceExecute = false) { }
        public virtual bool RequestVisualUpdateAsync(IScheduler scheduler, EngineController engine, IRenderPackageFactory factory, bool forceUpdate = false) { throw null; }
        public virtual bool RequestVisualUpdateAsync(IScheduler scheduler, EngineController engine, IRenderPackageFactory factory, bool forceUpdate, bool ignoreIsVisible = false) { throw null; }
        public void Warning(string p, bool isPersistent = false) { }
        public override void Dispose() { }
        protected override void SerializeCore(XmlElement element, Dynamo.Graph.SaveContext context) { }
        protected override void DeserializeCore(XmlElement nodeElement, Dynamo.Graph.SaveContext context) { }
        internal bool WasRenderPackageUpdatedAfterExecution { get; set; }
        // Private setter in Dynamo (changed through UpdateValue only).
        public bool IsVisible { get { throw null; } private set { } }
    }

    [AttributeUsage(AttributeTargets.All)] public class NodeNameAttribute : Attribute { public NodeNameAttribute(string elementName) { } }
    [AttributeUsage(AttributeTargets.All)] public class NodeCategoryAttribute : Attribute { public NodeCategoryAttribute(string category) { } }
    [AttributeUsage(AttributeTargets.All)] public class NodeDescriptionAttribute : Attribute { public NodeDescriptionAttribute(string description) { } }
    [AttributeUsage(AttributeTargets.All)] public class NodeSearchTagsAttribute : Attribute { public NodeSearchTagsAttribute(params string[] tags) { } }
    [AttributeUsage(AttributeTargets.All)] public class IsDesignScriptCompatibleAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public class InPortNamesAttribute : Attribute { public InPortNamesAttribute(params string[] names) { } }
    [AttributeUsage(AttributeTargets.Class)] public class InPortTypesAttribute : Attribute { public InPortTypesAttribute(params string[] types) { } }
    [AttributeUsage(AttributeTargets.Class)] public class InPortDescriptionsAttribute : Attribute { public InPortDescriptionsAttribute(params string[] descriptions) { } }
    [AttributeUsage(AttributeTargets.Class)] public class OutPortNamesAttribute : Attribute { public OutPortNamesAttribute(params string[] names) { } }
    [AttributeUsage(AttributeTargets.Class)] public class OutPortTypesAttribute : Attribute { public OutPortTypesAttribute(params string[] types) { } }
    [AttributeUsage(AttributeTargets.Class)] public class OutPortDescriptionsAttribute : Attribute { public OutPortDescriptionsAttribute(params string[] descriptions) { } }
}

namespace Dynamo.Graph.Connectors
{
    public class ConnectorModel : Dynamo.Graph.ModelBase
    {
        public PortModel Start { get { throw null; } }
        public PortModel End { get { throw null; } }
        protected override void SerializeCore(XmlElement element, Dynamo.Graph.SaveContext context) { }
        protected override void DeserializeCore(XmlElement nodeElement, Dynamo.Graph.SaveContext context) { }
    }
}

namespace Dynamo.Graph.Workspaces
{
    public abstract class WorkspaceModel : Dynamo.Core.NotificationObject
    {
        public event Action<NodeModel> NodeRemoved;
        public IEnumerable<NodeModel> Nodes { get { throw null; } }
        public void MarkAsIndependentlyModified() { }
    }
}

namespace Dynamo.Selection
{
    // internal in DynamoCore: unusable from a package.
    internal class DynamoSelection
    {
        public static DynamoSelection Instance { get { throw null; } }
        public ObservableCollection<object> Selection { get { throw null; } }
    }
}

namespace Dynamo.Interfaces
{
    public interface IPreferences { }
}

namespace Dynamo.Models
{
    public delegate void DynamoModelHandler(DynamoModel model);

    public interface IEngineControllerManager
    {
        EngineController EngineController { get; }
    }

    public interface IDynamoModel
    {
        IEnumerable<WorkspaceModel> Workspaces { get; }
        event DynamoModelHandler ShutdownStarted;
    }

    public class DynamoModel : IDynamoModel, IEngineControllerManager
    {
        public readonly DynamoLogger Logger;
        public IEnumerable<WorkspaceModel> Workspaces { get { throw null; } }
        public EngineController EngineController { get { throw null; } }
        public event DynamoModelHandler ShutdownStarted;
    }
}

namespace Dynamo.UI.Commands
{
    public class DelegateCommand : ICommand
    {
        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) { throw null; }
        public void Execute(object parameter) { }
    }
}

namespace Dynamo.Wpf.ViewModels
{
    public class RenderPackageFactoryViewModel
    {
        public IRenderPackageFactory Factory { get { throw null; } }
    }
}

namespace Dynamo.ViewModels
{
    public interface IDynamoViewModel : INotifyPropertyChanged { }

    public partial class DynamoViewModel : Dynamo.Core.NotificationObject, IDynamoViewModel
    {
        public Window Owner { get; set; }
        public DynamoModel Model { get { throw null; } }
        public Dynamo.Wpf.ViewModels.RenderPackageFactoryViewModel RenderPackageFactoryViewModel { get; set; }
        public Dynamo.Wpf.ViewModels.Watch3D.DefaultWatch3DViewModel BackgroundPreviewViewModel { get { throw null; } }
        internal System.Windows.Threading.Dispatcher UIDispatcher { get; set; }
    }

    public partial class WorkspaceViewModel : Dynamo.Core.NotificationObject
    {
        public readonly WorkspaceModel Model;
    }

    public partial class NodeViewModel : Dynamo.Core.NotificationObject
    {
        public readonly DynamoViewModel DynamoViewModel;
        public readonly WorkspaceViewModel WorkspaceViewModel;
        public NodeModel NodeModel { get { throw null; } }
    }
}

namespace Dynamo.Controls
{
    public partial class NodeView : UserControlStub
    {
        public Dynamo.ViewModels.NodeViewModel ViewModel { get { throw null; } }
        public Grid ContentGrid { get { throw null; } }
        public ContextMenu MainContextMenu;
        internal Grid centralGrid;
    }

    // stands in for System.Windows.Controls.UserControl
    public class UserControlStub : ContentControl { }
}

namespace Dynamo.Wpf
{
    public interface INodeViewCustomization<in T> : IDisposable where T : NodeModel
    {
        void CustomizeView(T model, Dynamo.Controls.NodeView nodeView);
    }
}

namespace Dynamo.Wpf.ViewModels.Watch3D
{
    public class Watch3DViewModelStartupParams
    {
        public Watch3DViewModelStartupParams() { }
        public Watch3DViewModelStartupParams(DynamoModel model) { }
    }

    public class CameraData
    {
        public string Name { get; set; }
        public double EyeX { get; set; }
        public double EyeY { get; set; }
        public double EyeZ { get; set; }
        public double LookX { get; set; }
        public double LookY { get; set; }
        public double LookZ { get; set; }
        public double UpX { get; set; }
        public double UpY { get; set; }
        public double UpZ { get; set; }
    }

    public class DefaultWatch3DViewModel : Dynamo.Core.NotificationObject, IDisposable
    {
        protected readonly NodeModel watchModel;
        protected readonly IDynamoModel dynamoModel;
        protected readonly IScheduler scheduler;
        protected readonly ILogger logger;
        protected readonly IEngineControllerManager engineManager;
        protected IRenderPackageFactory renderPackageFactory;
        protected bool active;

        public DefaultWatch3DViewModel(NodeModel model, Watch3DViewModelStartupParams parameters) { }

        public string Name { get; set; }
        public virtual string PreferenceWatchName { get { throw null; } }
        public virtual bool IsBackgroundPreview { get { throw null; } }
        public virtual bool Active { get; set; }
        public Dynamo.UI.Commands.DelegateCommand ZoomToFitCommand { get; set; }
        public event Action<object, RoutedEventArgs> ViewCameraChanged;

        public void Setup(Dynamo.ViewModels.IDynamoViewModel viewModel, IRenderPackageFactory renderPackageFactory) { }
        public void RegenerateAllPackages() { }
        public virtual void DeleteGeometryForNode(NodeModel node, bool requestUpdate = true) { }
        public virtual void GenerateViewGeometryFromRenderPackagesAndRequestUpdate(RenderPackageCache taskPackages) { }

        protected virtual void OnClear() { }
        protected void UnregisterEventHandlers() { }
        protected internal void UnregisterNodeEventHandlers(NodeModel node) { }
        protected void RegisterPortEventHandlers(NodeModel node) { }
        protected void OnWorkspaceRemoved(WorkspaceModel workspace) { }
        protected virtual void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e) { }
        protected virtual void OnNodePropertyChanged(object sender, PropertyChangedEventArgs e) { }
        protected virtual void OnRenderPackagesUpdated(NodeModel node, RenderPackageCache packages) { }
        protected virtual void OnWorkspaceSaving(XmlDocument doc) { }
        protected virtual void OnWorkspaceOpening(object obj) { }
        protected virtual void PortConnectedHandler(PortModel arg1, ConnectorModel arg2) { }
        protected virtual void PortDisconnectedHandler(PortModel port) { }
        protected virtual void SelectionChangedHandler(object sender, NotifyCollectionChangedEventArgs e) { }
        protected virtual void HandleViewClick(object sender, MouseButtonEventArgs e) { }
        protected virtual void ZoomToFit(object parameter) { }
        protected virtual void Dispose(bool disposing) { }
        public void Dispose() { }
        internal void OnViewMouseDown(object sender, MouseButtonEventArgs e) { }
    }

    public class HelixWatch3DViewModel : DefaultWatch3DViewModel
    {
        protected HelixWatch3DViewModel(NodeModel model, Watch3DViewModelStartupParams parameters) : base(model, parameters) { }

        public HelixToolkit.Wpf.SharpDX.PerspectiveCamera Camera { get; set; }
        public IEffectsManager EffectsManager { get { throw null; } }
        public IEnumerable<Element3D> SceneItems { get { throw null; } }
        public bool IsResizable { get; protected set; }

        public event Action RequestViewRefresh;
        public event Action<RenderPackageCache, bool> RequestCreateModels;
        public event Action<NodeModel> RequestRemoveModels;
        public event Action<BoundingBox> RequestZoomToFit;

        public void SetCameraData(CameraData data) { }
        public CameraData GetCameraInformation() { throw null; }
        protected void OnRequestZoomToFit(BoundingBox bounds) { }

        protected internal virtual void UpdateUpstream() { }
        protected internal virtual void OnWatchExecution() { }
        protected override void OnClear() { }
        protected override void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e) { }
        protected override void OnNodePropertyChanged(object sender, PropertyChangedEventArgs e) { }
        protected override void OnWorkspaceSaving(XmlDocument doc) { }
        protected override void OnWorkspaceOpening(object obj) { }
        protected override void SelectionChangedHandler(object sender, NotifyCollectionChangedEventArgs e) { }
        protected override void HandleViewClick(object sender, MouseButtonEventArgs e) { }
        protected override void ZoomToFit(object parameter) { }
        protected override void Dispose(bool disposing) { }
        public override void DeleteGeometryForNode(NodeModel node, bool requestUpdate = true) { }
        public override void GenerateViewGeometryFromRenderPackagesAndRequestUpdate(RenderPackageCache taskPackages) { }
        internal void UpdateNearClipPlane() { }
        internal void ComputeFrameUpdate() { }
    }
}
