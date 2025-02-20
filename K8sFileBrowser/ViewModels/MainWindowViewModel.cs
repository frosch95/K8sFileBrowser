using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading.Tasks;
using K8sFileBrowser.Models;
using K8sFileBrowser.Services;
using Microsoft.IdentityModel.Tokens;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace K8sFileBrowser.ViewModels;

public class MainWindowViewModel : ViewModelBase
{

  #region Properties

  [Reactive]
  public string? Version { get; set; }

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
  
  private string _lastDirectory = ".";

  #endregion Properties

  #region Commands

  public ReactiveCommand<Unit, Unit> DownloadCommand { get; private set; } = null!;
  public ReactiveCommand<Unit, Unit> DownloadLogCommand { get; private set; } = null!;
  public ReactiveCommand<Unit, Unit> RefreshCommand { get; private set; } = null!;
  public ReactiveCommand<Unit, Unit> ParentCommand { get; private set; } = null!;
  public ReactiveCommand<Unit, Unit> OpenCommand { get; private set; } = null!;
  private ReactiveCommand<Namespace, IEnumerable<Pod>> GetPodsForNamespace { get; set; } = null!;

  #endregion Commands

  public MainWindowViewModel()
  {
    IKubernetesService kubernetesService = new KubernetesService();

    Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();

    // commands
    ConfigureOpenDirectoryCommand();
    ConfigureDownloadFileCommand(kubernetesService);
    ConfigureRefreshCommand(kubernetesService);
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

  #region Property Subscriptions

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
        ResetNamespaces();
        ClusterContexts = x.OrderBy(c => c.Name);

        // select the current cluster context
        SelectedClusterContext = ClusterContexts
          .FirstOrDefault(c => c.Name == kubernetesService.GetCurrentContext());
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
        ResetPods();
        Namespaces = ns.OrderBy(n => n.Name);
      });
  }

  private void RegisterReadPods()
  {
    // read the pods when the namespace changes
    this
      .WhenAnyValue(c => c.SelectedNamespace)
      .Throttle(new TimeSpan(10))
      .Where(x => x != null)
      .SelectMany(ns => GetPodsForNamespace.Execute(ns!))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(x =>
      {
        ResetContainers();
        Pods = x.OrderBy(p => p.Name);
      });
  }

  private void RegisterReadContainers()
  {
    // read the file information when the path changes
    this
      .WhenAnyValue(c => c.SelectedPod)
      .Throttle(new TimeSpan(10))
      .Where(x => x != null)
      .Select(x => x!.Containers.Select(c => new Container {Name = c}))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe( x =>
      {
        ResetPath();
        Containers = x;
      });

    this.WhenAnyValue(x => x.Containers)
      .Throttle(new TimeSpan(10))
      .Where(x => x != null && x.Any())
      .ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(x => SelectedContainer = x?.FirstOrDefault());
  }

  private void RegisterResetPath()
  {
    // reset the path when the pod or namespace changes
    this.WhenAnyValue(c => c.SelectedContainer)
      .Throttle(new TimeSpan(10))
      .Where(x => x != null)
      .ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(_ => SelectedPath = "/");
  }

  private void RegisterReadFiles(IKubernetesService kubernetesService)
  {
    // read the file information when the path changes
    this
      .WhenAnyValue(c => c.SelectedContainer, c => c.SelectedPath)
      .Throttle(new TimeSpan(10))
      .Where(x => x is { Item1: not null, Item2: not null })
      .Select(x => GetFileInformation(kubernetesService, x.Item2!, SelectedPod!, SelectedNamespace!, x.Item1!))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(x => FileInformation = x);
  }
  #endregion Property Subscriptions

  #region Configure Commands
  private void ConfigureGetPodsForNamespaceCommand(IKubernetesService kubernetesService)
  {
    GetPodsForNamespace = ReactiveCommand.CreateFromObservable<Namespace, IEnumerable<Pod>>(ns =>
      Observable.StartAsync(_ => PodsAsync(ns, kubernetesService), RxApp.TaskpoolScheduler));

    GetPodsForNamespace.ThrownExceptions.ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(ShowErrorMessage);
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
      .Subscribe(ShowErrorMessage);
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
        var saveFileName = await ApplicationHelper.SaveFile(_lastDirectory, fileName);
        if (saveFileName != null)
        {
          SetLastDirectory(saveFileName);
          ShowWorkingMessage("Downloading Log...");
          await kubernetesService.DownloadLog(SelectedNamespace, SelectedPod, SelectedContainer, saveFileName);
          HideWorkingMessage();
        }
      }, RxApp.TaskpoolScheduler);
    }, isSelectedPod, RxApp.MainThreadScheduler);

    DownloadLogCommand.ThrownExceptions.ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(ShowErrorMessage);
  }
  
  private void ConfigureRefreshCommand(IKubernetesService kubernetesService)
  {
    var isSelectedContainer = this
      .WhenAnyValue(x => x.SelectedContainer)
      .Select(x => x != null);

    RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
    {
      await Observable.Start(() =>
      {
        FileInformation = GetFileInformation(kubernetesService, SelectedPath!, SelectedPod!, SelectedNamespace!, SelectedContainer!);
      }, RxApp.TaskpoolScheduler);
    }, isSelectedContainer, RxApp.MainThreadScheduler);

    RefreshCommand.ThrownExceptions.ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(ShowErrorMessage);
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
        var saveFileName = await ApplicationHelper.SaveFile(_lastDirectory, fileName);
        if (saveFileName != null)
        {
          SetLastDirectory(saveFileName);
          ShowWorkingMessage("Downloading File...");
          await kubernetesService.DownloadFile(SelectedNamespace, SelectedPod, SelectedContainer, SelectedFile, saveFileName);
          HideWorkingMessage();
        }
      }, RxApp.TaskpoolScheduler);
    }, isFile, RxApp.MainThreadScheduler);

    DownloadCommand.ThrownExceptions.ObserveOn(RxApp.MainThreadScheduler)
      .Subscribe(ShowErrorMessage);
  }

  private void SetLastDirectory(string saveFileName)
  {
    _lastDirectory = saveFileName.Substring(0, saveFileName.LastIndexOf(Path.DirectorySeparatorChar));
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
      .Subscribe(ShowErrorMessage);
  }

  #endregion Configure Commands

  #region Get Data

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
      ShowErrorMessage(e);
      return new List<Namespace>();
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

  #endregion Get Data

  #region Reset Data

  private void ResetPath()
  {
    FileInformation = new List<FileInformation>();
    SelectedPath = null;
    SelectedContainer = null;
  }

  private void ResetContainers()
  {
    ResetPath();
    Containers = new List<Container>();
    SelectedPod = null;

  }

  private void ResetPods()
  {
    ResetContainers();
    SelectedNamespace = null;
    Pods = new List<Pod>();

  }

  private void ResetNamespaces()
  {
    ResetPods();
    Namespaces = new List<Namespace>();
    SelectedClusterContext = null;
  }

  #endregion Reset Data

  #region show messages

  private void ShowWorkingMessage(string message)
  {
    RxApp.MainThreadScheduler.Schedule(Action);
    return;

    void Action()
    {
      Message = new Message
      {
        IsVisible = true,
        Text = message,
        IsError = false
      };
    }
  }

  private void ShowErrorMessage(string message)
  {
    RxApp.MainThreadScheduler.Schedule(Action);
    return;

    async void Action()
    {
      Message = new Message { IsVisible = true, Text = message, IsError = true };
      await Task.Delay(7000);
      HideWorkingMessage();
    }
  }

  private void ShowErrorMessage(Exception exception)
  {
    // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
    Log.Error(exception, exception.Message);
    ShowErrorMessage(exception.Message);
  }

  private void HideWorkingMessage()
  {
    RxApp.MainThreadScheduler.Schedule(() => Message = new Message
    {
      IsVisible = false,
      Text = "",
      IsError = false
    });
  }

  #endregion show messages
}