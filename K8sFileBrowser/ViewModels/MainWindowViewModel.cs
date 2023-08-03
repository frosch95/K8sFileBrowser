using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using K8sFileBrowser.Models;
using K8sFileBrowser.Services;
using ReactiveUI;

namespace K8sFileBrowser.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
  private ObservableAsPropertyHelper<IEnumerable<ClusterContext>> _clusterContexts;
  public IEnumerable<ClusterContext> ClusterContexts => _clusterContexts.Value;

  private ClusterContext? _selectedClusterContext;

  public ClusterContext? SelectedClusterContext
  {
    get => _selectedClusterContext;
    set => this.RaiseAndSetIfChanged(ref _selectedClusterContext, value);
  }

  private IEnumerable<Namespace> _namespaces;
  public IEnumerable<Namespace> Namespaces
  {
    get => _namespaces;
    set => this.RaiseAndSetIfChanged(ref _namespaces, value);
  }

  private Namespace? _selectedNamespace;

  public Namespace? SelectedNamespace
  {
    get => _selectedNamespace;
    set => this.RaiseAndSetIfChanged(ref _selectedNamespace, value);
  }

  private ObservableAsPropertyHelper<IEnumerable<Pod>> _pods;
  public IEnumerable<Pod> Pods => _pods.Value;

  private Pod? _selectedPod;

  public Pod? SelectedPod
  {
    get => _selectedPod;
    set => this.RaiseAndSetIfChanged(ref _selectedPod, value);
  }

  private ObservableAsPropertyHelper<IEnumerable<FileInformation>> _fileInformation;
  public IEnumerable<FileInformation> FileInformation => _fileInformation.Value;

  private FileInformation? _selectedFile;

  public FileInformation? SelectedFile
  {
    get => _selectedFile;
    set => this.RaiseAndSetIfChanged(ref _selectedFile, value);
  }

  private string? _selectedPath;

  public string? SelectedPath
  {
    get => _selectedPath;
    set => this.RaiseAndSetIfChanged(ref _selectedPath, value);
  }

  private Message _message;
  public Message Message
  {
    get => _message;
    set => this.RaiseAndSetIfChanged(ref _message, value);
  }
  
  public ReactiveCommand<Unit, Unit> DownloadCommand { get; private set; }
  public ReactiveCommand<Unit, Unit> DownloadLogCommand { get; private set; }
  public ReactiveCommand<Unit, Unit> ParentCommand { get; private set; }
  public ReactiveCommand<Unit, Unit> OpenCommand { get; private set; }

  private ReactiveCommand<Namespace, IEnumerable<Pod>> GetPodsForNamespace { get; set; }


  public MainWindowViewModel()
  {
    //TODO: use dependency injection to get the kubernetes service
    IKubernetesService kubernetesService = new KubernetesService();

    // commands
    ConfigureOpenDirectoryCommand();
    ConfigureDownloadFileCommand(kubernetesService);
    ConfigureDownloadLogCommand(kubernetesService);
    ConfigureParentDirectoryCommand();
    ConfigureGetPodsForNamespaceCommand(kubernetesService);

    // register the listeners
    RegisterReadNamespaces(kubernetesService);
    RegisterReadPods();
    RegisterReadFiles(kubernetesService);
    RegisterResetPath();

    // load the cluster contexts
    InitiallyLoadContexts(kubernetesService);
  }

  private void InitiallyLoadContexts(IKubernetesService kubernetesService)
  {
    // load the cluster contexts when the view model is created
    var loadContexts = ReactiveCommand
      .Create<Unit, IEnumerable<ClusterContext>>(_ => kubernetesService.GetClusterContexts());
    _clusterContexts = loadContexts.Execute().ToProperty(
      this, x => x.ClusterContexts, scheduler: RxApp.MainThreadScheduler);

    // select the current cluster context
    SelectedClusterContext = ClusterContexts
      .FirstOrDefault(x => x.Name == kubernetesService.GetCurrentContext());
  }

  private void RegisterResetPath()
  {
    // reset the path when the pod or namespace changes
    this.WhenAnyValue(c => c.SelectedPod, c => c.SelectedNamespace)
      .Throttle(new TimeSpan(10))
      .Subscribe(x => SelectedPath = "/");
  }

  private void RegisterReadFiles(IKubernetesService kubernetesService)
  {
    // read the file information when the path changes
    _fileInformation = this
      .WhenAnyValue(c => c.SelectedPath, c => c.SelectedPod, c => c.SelectedNamespace)
      .Throttle(new TimeSpan(10))
      .Select(x => x.Item3 == null || x.Item2 == null
        ? new List<FileInformation>()
        : kubernetesService.GetFiles(x.Item3!.Name, x.Item2!.Name, x.Item2!.Containers.First(),
          x.Item1))
      .ObserveOn(RxApp.MainThreadScheduler)
      .ToProperty(this, x => x.FileInformation);
  }

  private void RegisterReadPods()
  {
    // read the pods when the namespace changes
    _pods = this
      .WhenAnyValue(c => c.SelectedNamespace)
      .Throttle(new TimeSpan(10))
      .SelectMany(ns => GetPodsForNamespace.Execute(ns))
      .ObserveOn(RxApp.MainThreadScheduler)
      .ToProperty(this, x => x.Pods);
  }

  private void RegisterReadNamespaces(IKubernetesService kubernetesService)
  {
    // read the cluster contexts
    this
      .WhenAnyValue(c => c.SelectedClusterContext)
      .Throttle(new TimeSpan(10))
      .SelectMany(context => GetClusterContextAsync(context, kubernetesService))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(ns => Namespaces = ns);
  }

  private void ConfigureGetPodsForNamespaceCommand(IKubernetesService kubernetesService)
  {
    GetPodsForNamespace = ReactiveCommand.CreateFromObservable<Namespace, IEnumerable<Pod>>(ns =>
      Observable.StartAsync(_ => PodsAsync(ns, kubernetesService), RxApp.TaskpoolScheduler));

    GetPodsForNamespace.ThrownExceptions
      .Subscribe(ex => ShowErrorMessage(ex.Message).ConfigureAwait(false).GetAwaiter().GetResult());
  }

  private void ConfigureParentDirectoryCommand()
  {
    var isNotRoot = this
      .WhenAnyValue(x => x.SelectedPath, x => x.Message.IsVisible)
      .Select(x => x.Item1 is not "/" && !x.Item2);

    ParentCommand = ReactiveCommand.Create(() =>
    {
      SelectedPath = SelectedPath![..SelectedPath!.LastIndexOf('/')];
      if (SelectedPath!.Length == 0)
      {
        SelectedPath = "/";
      }
    }, isNotRoot, RxApp.MainThreadScheduler);

    ParentCommand.ThrownExceptions
      .Subscribe(ex => ShowErrorMessage(ex.Message).ConfigureAwait(false).GetAwaiter().GetResult());
  }

  private void ConfigureDownloadLogCommand(IKubernetesService kubernetesService)
  {
    var isSelectedPod = this
      .WhenAnyValue(x => x.SelectedPod)
      .Select(x => x != null);

    DownloadLogCommand = ReactiveCommand.CreateFromTask(async () =>
    {
      await Observable.StartAsync(async () =>
      {
        var fileName = SelectedPod?.Name + ".log";
        var saveFileName = await ApplicationHelper.SaveFile(".", fileName);
        if (saveFileName != null)
        {
          ShowWorkingMessage("Downloading Log...");
          await kubernetesService.DownloadLog(SelectedNamespace, SelectedPod, saveFileName);
          HideWorkingMessage();
        }
      }, RxApp.TaskpoolScheduler);
    }, isSelectedPod, RxApp.MainThreadScheduler);

    DownloadLogCommand.ThrownExceptions
      .Subscribe(ex => ShowErrorMessage(ex.Message).ConfigureAwait(false).GetAwaiter().GetResult());
  }

  private void ConfigureDownloadFileCommand(IKubernetesService kubernetesService)
  {
    var isFile = this
      .WhenAnyValue(x => x.SelectedFile, x => x.Message.IsVisible)
      .Select(x => x is { Item1.Type: FileType.File, Item2: false });

    DownloadCommand = ReactiveCommand.CreateFromTask(async () =>
    {
      await Observable.StartAsync(async () =>
      {
        var fileName = SelectedFile!.Name.Substring(SelectedFile!.Name.LastIndexOf('/') + 1,
          SelectedFile!.Name.Length - SelectedFile!.Name.LastIndexOf('/') - 1);
        var saveFileName = await ApplicationHelper.SaveFile(".", fileName);
        if (saveFileName != null)
        {
          ShowWorkingMessage("Downloading File...");
          await kubernetesService.DownloadFile(SelectedNamespace, SelectedPod, SelectedFile, saveFileName);
          HideWorkingMessage();
        }
      }, RxApp.TaskpoolScheduler);
    }, isFile, RxApp.MainThreadScheduler);

    DownloadCommand.ThrownExceptions
      .Subscribe(ex => ShowErrorMessage(ex.Message).ConfigureAwait(false).GetAwaiter().GetResult());
  }

  private void ConfigureOpenDirectoryCommand()
  {
    var isDirectory = this
      .WhenAnyValue(x => x.SelectedFile, x => x.Message.IsVisible)
      .Select(x => x is { Item1.Type: FileType.Directory, Item2: false });

    OpenCommand = ReactiveCommand.Create(() => { SelectedPath = SelectedFile != null ? SelectedFile!.Name : "/"; },
      isDirectory, RxApp.MainThreadScheduler);

    OpenCommand.ThrownExceptions
      .Subscribe(ex => ShowErrorMessage(ex.Message).ConfigureAwait(false).GetAwaiter().GetResult());
  }

  private static async Task<IEnumerable<Pod>> PodsAsync(Namespace? ns, IKubernetesService kubernetesService)
  {
    if (ns == null)
      return new List<Pod>();
    return await kubernetesService.GetPodsAsync(ns.Name);
  }

  private async Task<IEnumerable<Namespace>> GetClusterContextAsync(ClusterContext? context, IKubernetesService kubernetesService)
  {
    if (context == null)
      return new List<Namespace>();

    try
    {
      ShowWorkingMessage("Switching context...");
      Namespaces = new List<Namespace>();
      kubernetesService.SwitchClusterContext(context!);
      var namespaces = await kubernetesService.GetNamespacesAsync();
      HideWorkingMessage();
      return namespaces;
    }
    catch (Exception e)
    {
      await ShowErrorMessage(e.Message);      
      return new List<Namespace>();
    }
  }

  private void ShowWorkingMessage(string message)
  {
    Message = new Message
    {
      IsVisible = true,
      Text = message,
      IsError = false
    };
  }
  
  private async Task ShowErrorMessage(string message)
  {
    Message = new Message
    {
      IsVisible = true,
      Text = message,
      IsError = true
    };
    await Task.Delay(7000);
    HideWorkingMessage();
  }
  
  private void HideWorkingMessage()
  {
    Message = new Message
    {
      IsVisible = false,
      Text = "",
      IsError = false
    };
  }
}