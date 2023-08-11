using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using K8sFileBrowser.Models;
using K8sFileBrowser.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace K8sFileBrowser.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
  [Reactive]
  public IEnumerable<ClusterContext> ClusterContexts { get; set; } = null!;

  [Reactive]
  public ClusterContext? SelectedClusterContext { get; set; }

  [Reactive]
  public IEnumerable<Namespace> Namespaces { get; set; } = null!;

  [Reactive]
  public Namespace? SelectedNamespace { get; set; }

  [Reactive]
  public IEnumerable<Pod> Pods  { get; set; } = null!;

  [Reactive]
  public Pod? SelectedPod { get; set; }

  [Reactive]
  public IEnumerable<Container>? Containers { get; set; }

  [Reactive]
  public Container? SelectedContainer { get; set; }

  [Reactive]
  public IEnumerable<FileInformation> FileInformation { get; set; } = null!;

  [Reactive]
  public FileInformation? SelectedFile { get; set; }

  [Reactive]
  public string? SelectedPath { get; set; }

  [Reactive]
  public Message Message { get; set; } = null!;

  public ReactiveCommand<Unit, Unit> DownloadCommand { get; private set; } = null!;
  public ReactiveCommand<Unit, Unit> DownloadLogCommand { get; private set; } = null!;
  public ReactiveCommand<Unit, Unit> ParentCommand { get; private set; } = null!;
  public ReactiveCommand<Unit, Unit> OpenCommand { get; private set; } = null!;

  private ReactiveCommand<Namespace, IEnumerable<Pod>> GetPodsForNamespace { get; set; } = null!;


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
    RegisterReadContainers();
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
    loadContexts.Execute()
      .Throttle(new TimeSpan(10))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(x =>
      {
        ClusterContexts = x;
        // select the current cluster context
        SelectedClusterContext = ClusterContexts
          .FirstOrDefault(x => x.Name == kubernetesService.GetCurrentContext());
      });
  }

  private void RegisterResetPath()
  {
    // reset the path when the pod or namespace changes
    this.WhenAnyValue(c => c.SelectedContainer)
      .Throttle(new TimeSpan(10))
      .ObserveOn(RxApp.TaskpoolScheduler)
      .Subscribe(_ => SelectedPath = "/");
  }

  private void RegisterReadContainers()
  {
    // read the file information when the path changes
    this
      .WhenAnyValue(c => c.SelectedPod)
      .Throttle(new TimeSpan(10))
      .Select(x => x == null
        ? new List<Container>()
        : x.Containers.Select(c => new Container {Name = c}))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe( x =>
      {
        Containers = x;
        FileInformation = new List<FileInformation>();
      });

    this.WhenAnyValue(x => x.Containers)
      .Throttle(new TimeSpan(10))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(x => SelectedContainer = x?.FirstOrDefault());
}

  private void RegisterReadFiles(IKubernetesService kubernetesService)
  {
    // read the file information when the path changes
    this
      .WhenAnyValue(c => c.SelectedContainer, c => c.SelectedPath)
      .Throttle(new TimeSpan(10))
      .Select(x => x.Item1 == null || x.Item2 == null
        ? new List<FileInformation>()
        : GetFileInformation(kubernetesService, x.Item2, SelectedPod!, SelectedNamespace!, x.Item1))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(x => FileInformation = x);
  }

  private void RegisterReadPods()
  {
    // read the pods when the namespace changes
    this
      .WhenAnyValue(c => c.SelectedNamespace)
      .Throttle(new TimeSpan(10))
      .SelectMany(ns => GetPodsForNamespace.Execute(ns!))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(x =>
      {
        Pods = x;
        Containers = null;
        FileInformation = new List<FileInformation>();
      });
  }

  private void RegisterReadNamespaces(IKubernetesService kubernetesService)
  {
    // read the cluster contexts
    this
      .WhenAnyValue(c => c.SelectedClusterContext)
      .Throttle(new TimeSpan(10))
      .SelectMany(context => GetClusterContextAsync(context, kubernetesService))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(ns =>
      {
        Namespaces = ns;
        Pods = new List<Pod>();
        Containers = null;
        FileInformation = new List<FileInformation>();
      });
  }

  private void ConfigureGetPodsForNamespaceCommand(IKubernetesService kubernetesService)
  {
    GetPodsForNamespace = ReactiveCommand.CreateFromObservable<Namespace, IEnumerable<Pod>>(ns =>
      Observable.StartAsync(_ => PodsAsync(ns, kubernetesService), RxApp.TaskpoolScheduler));

    GetPodsForNamespace.ThrownExceptions.ObserveOn(RxApp.MainThreadScheduler)
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

    ParentCommand.ThrownExceptions.ObserveOn(RxApp.MainThreadScheduler)
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
          await kubernetesService.DownloadLog(SelectedNamespace, SelectedPod, SelectedContainer, saveFileName);
          HideWorkingMessage();
        }
      }, RxApp.TaskpoolScheduler);
    }, isSelectedPod, RxApp.MainThreadScheduler);

    DownloadLogCommand.ThrownExceptions.ObserveOn(RxApp.MainThreadScheduler)
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
          await kubernetesService.DownloadFile(SelectedNamespace, SelectedPod, SelectedContainer, SelectedFile, saveFileName);
          HideWorkingMessage();
        }
      }, RxApp.TaskpoolScheduler);
    }, isFile, RxApp.MainThreadScheduler);

    DownloadCommand.ThrownExceptions.ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(ex => ShowErrorMessage(ex.Message).ConfigureAwait(false).GetAwaiter().GetResult());
  }

  private void ConfigureOpenDirectoryCommand()
  {
    var isDirectory = this
      .WhenAnyValue(x => x.SelectedFile, x => x.Message.IsVisible)
      .Select(x => x is { Item1.Type: FileType.Directory, Item2: false });

    OpenCommand = ReactiveCommand.Create(() =>
      {
        if (".." == SelectedFile?.Name)
          SelectedPath = SelectedFile?.Parent;
        else
          SelectedPath = SelectedFile != null ? SelectedFile!.Name : "/";
      },
      isDirectory, RxApp.MainThreadScheduler);

    OpenCommand.ThrownExceptions.ObserveOn(RxApp.MainThreadScheduler)
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
      kubernetesService.SwitchClusterContext(context);
      var namespaces = await kubernetesService.GetNamespacesAsync();
      HideWorkingMessage();
      return namespaces;
    }
    catch (Exception e)
    {
      RxApp.MainThreadScheduler.Schedule(Action);
      return new List<Namespace>();

      async void Action() => await ShowErrorMessage(e.Message);
    }
  }

  private IList<FileInformation> GetFileInformation(IKubernetesService kubernetesService,
    string path, Pod pod, Namespace nameSpace, Container container)
  {
    var kubernetesFileInformation = kubernetesService.GetFiles(
      nameSpace.Name, pod.Name, container.Name, path);

    // when the path is root, we don't want to show the parent directory
    if (SelectedPath is not { Length: > 1 }) return kubernetesFileInformation;

    // add the parent directory
    var parent = SelectedPath[..SelectedPath.LastIndexOf('/')];
    if (string.IsNullOrEmpty(parent))
    {
      parent = "/";
    }

    return kubernetesFileInformation.Prepend(new FileInformation
    {
      Name = "..",
      Type = FileType.Directory,
      Parent = parent
    }).ToList();
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