using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using K8sFileBrowser.Models;
using K8sFileBrowser.Services;
using ReactiveUI;

namespace K8sFileBrowser.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly ObservableAsPropertyHelper<IEnumerable<ClusterContext>> _clusterContexts;
    public IEnumerable<ClusterContext> ClusterContexts => _clusterContexts.Value;

    private ClusterContext? _selectedClusterContext;
    public ClusterContext? SelectedClusterContext
    {
        get => _selectedClusterContext;
        set => this.RaiseAndSetIfChanged(ref _selectedClusterContext, value);
    }

    private readonly ObservableAsPropertyHelper<IEnumerable<Namespace>> _namespaces;
    public IEnumerable<Namespace> Namespaces => _namespaces.Value;

    private Namespace? _selectedNamespace;
    public Namespace? SelectedNamespace
    {
        get => _selectedNamespace;
        set => this.RaiseAndSetIfChanged(ref _selectedNamespace, value);
    }

    private readonly ObservableAsPropertyHelper<IEnumerable<Pod>> _pods;
    public IEnumerable<Pod> Pods => _pods.Value;

    private Pod? _selectedPod;
    public Pod? SelectedPod
    {
        get => _selectedPod;
        set => this.RaiseAndSetIfChanged(ref _selectedPod, value);
    }

    private readonly ObservableAsPropertyHelper<IEnumerable<FileInformation>> _fileInformation;
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

    private bool _isDownloadActive;
    public bool IsDownloadActive
    {
      get => _isDownloadActive;
      set => this.RaiseAndSetIfChanged(ref _isDownloadActive, value);
    }

    public ReactiveCommand<Unit, Unit> DownloadCommand { get; }
    public ReactiveCommand<Unit, Unit> ParentCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }


    public MainWindowViewModel()
    {
        var kubernetesService = new KubernetesService();

        var isFile = this
            .WhenAnyValue(x => x.SelectedFile, x => x.IsDownloadActive)
            .Select(x => x is { Item1.Type: FileType.File, Item2: false });

        var isDirectory = this
            .WhenAnyValue(x => x.SelectedFile, x => x.IsDownloadActive)
            .Select(x => x is { Item1.Type: FileType.Directory, Item2: false });

        var isNotRoot = this
            .WhenAnyValue(x => x.SelectedPath, x => x.IsDownloadActive)
            .Select(x => x.Item1 is not "/" && !x.Item2);

        OpenCommand = ReactiveCommand.Create(() =>
        {
            SelectedPath = SelectedFile != null ? SelectedFile!.Name : "/";
        }, isDirectory, RxApp.MainThreadScheduler);

        DownloadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
          await Observable.StartAsync(async () => {
            var fileName = SelectedFile!.Name.Substring(SelectedFile!.Name.LastIndexOf('/') + 1, SelectedFile!.Name.Length - SelectedFile!.Name.LastIndexOf('/') - 1);
            var saveFileName = await ApplicationHelper.SaveFile(".", fileName);
            if (saveFileName != null)
            {
                IsDownloadActive = true;
                await kubernetesService.DownloadFile(SelectedNamespace, SelectedPod, SelectedFile, saveFileName);
                IsDownloadActive = false;
            }
          }, RxApp.TaskpoolScheduler);
        }, isFile, RxApp.MainThreadScheduler);

        ParentCommand = ReactiveCommand.Create(() =>
        {
            SelectedPath = SelectedPath![..SelectedPath!.LastIndexOf('/')];
            if (SelectedPath!.Length == 0)
            {
                SelectedPath = "/";
            }
        }, isNotRoot, RxApp.MainThreadScheduler);

        // read the cluster contexts
        _namespaces = this
            .WhenAnyValue(c => c.SelectedClusterContext)
            .Throttle(TimeSpan.FromMilliseconds(10))
            .Where(context => context != null)
            .Select(context =>
            {
                kubernetesService.SwitchClusterContext(context!);
                return kubernetesService.GetNamespaces();
            })
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, x => x.Namespaces);

        // read the pods when the namespace changes
        _pods = this
            .WhenAnyValue(c => c.SelectedNamespace)
            .Throttle(TimeSpan.FromMilliseconds(10))
            .Where(ns => ns != null)
            .Select(ns => kubernetesService.GetPods(ns!.Name))
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, x => x.Pods);

        // read the file information when the path changes
        _fileInformation = this
            .WhenAnyValue(c => c.SelectedPath, c => c.SelectedPod, c => c.SelectedNamespace)
            .Throttle(TimeSpan.FromMilliseconds(10))
            .Select(x => x.Item3 == null || x.Item2 == null
                ? new List<FileInformation>()
                : kubernetesService.GetFiles(x.Item3!.Name, x.Item2!.Name, x.Item2!.Containers.First(),
                    x.Item1))
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, x => x.FileInformation);

        // reset the path when the pod or namespace changes
        this.WhenAnyValue(c => c.SelectedPod, c => c.SelectedNamespace)
            .Subscribe(x => SelectedPath = "/");

        // load the cluster contexts when the view model is created
        var loadContexts = ReactiveCommand
            .Create<Unit, IEnumerable<ClusterContext>>(_ => kubernetesService.GetClusterContexts());
        _clusterContexts = loadContexts.Execute().ToProperty(
            this, x => x.ClusterContexts, scheduler: RxApp.MainThreadScheduler);

        // select the current cluster context
        SelectedClusterContext = ClusterContexts
            .FirstOrDefault(x => x.Name == kubernetesService.GetCurrentContext());
    }
}