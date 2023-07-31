﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Tar;
using k8s;
using k8s.KubeConfigModels;
using K8sFileBrowser.Models;
using Serilog;
using static ICSharpCode.SharpZipLib.Core.StreamUtils;

namespace K8sFileBrowser.Services;

public class KubernetesService
{
    private readonly K8SConfiguration _k8SConfiguration;
    private IKubernetes _kubernetesClient = null!;

    public KubernetesService()
    {
        _k8SConfiguration = KubernetesClientConfiguration.LoadKubeConfig();
        CreateKubernetesClient();
    }

    public IEnumerable<ClusterContext> GetClusterContexts()
    {
        return _k8SConfiguration.Contexts.Select(c => new ClusterContext { Name = c.Name }).ToList();
    }

    public string GetCurrentContext()
    {
        return _k8SConfiguration.CurrentContext;
    }

    public void SwitchClusterContext(ClusterContext clusterContext)
    {
        CreateKubernetesClient(clusterContext);
    }

    public IEnumerable<Namespace> GetNamespaces()
    {
        var namespaces = _kubernetesClient.CoreV1.ListNamespace();
        var namespaceList = namespaces != null
            ? namespaces.Items.Select(n => new Namespace { Name = n.Metadata.Name }).ToList()
            : new List<Namespace>();
        return namespaceList;
    }

    public IEnumerable<Pod> GetPods(string namespaceName)
    {
        var pods = _kubernetesClient.CoreV1.ListNamespacedPod(namespaceName);
        var podList = pods != null
            ? pods.Items.Select(n =>
                new Pod
                {
                    Name = n.Metadata.Name,
                    Containers = n.Spec.Containers.Select(c => c.Name).ToList()
                }).ToList()
            : new List<Pod>();
        return podList;
    }

    public IList<FileInformation> GetFiles(string namespaceName, string podName, string containerName, string path)
    {
        try
        {
            var execResult = new KubernetesFileInformationResult(path);
            var resultCode = _kubernetesClient
                .NamespacedPodExecAsync(
                    podName, namespaceName, containerName,
                    new[] { "find", path, "-maxdepth", "1", "-exec", "stat", "-c", "%F|%n|%s|%Y", "{}", ";" },
                    true,
                    execResult.ParseFileInformationCallback, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            return execResult.FileInformations
                .Where(f => f.Name != "." && f.Name != path)
                .OrderBy(f => f.Type).ThenBy(f => f.Name)
                .ToList();
        }
        catch (Exception e)
        {
            Log.Error(e, "exception while getting files");
            return new List<FileInformation>();
        }
    }

    private void CreateKubernetesClient(ClusterContext? clusterContext = null)
    {
        var clusterContextName = clusterContext == null ? _k8SConfiguration.CurrentContext : clusterContext.Name;
        var kubernetesClientConfiguration =
            KubernetesClientConfiguration.BuildConfigFromConfigFile(currentContext: clusterContextName);
        var kubernetesClient = new Kubernetes(kubernetesClientConfiguration);
        _kubernetesClient = kubernetesClient;
    }


    public async Task DownloadFile(Namespace? selectedNamespace, Pod? selectedPod, FileInformation selectedFile,
        string? saveFileName)
    {
        Log.Information("{SelectedNamespace} - {SelectedPod} - {SelectedFile} - {SaveFileName}", 
            selectedNamespace, selectedPod, selectedFile, saveFileName);
        var handler = new ExecAsyncCallback(async (_, stdOut, stdError) =>
        {
            try
            {
                await using var outputFileStream = File.OpenWrite(saveFileName!);
                await using var tarInputStream = new TarInputStream(stdOut, Encoding.Default);

                var entry = await tarInputStream.GetNextEntryAsync(default);
                if (entry == null)
                {
                    throw new IOException("Copy command failed: no files found");
                }
                
                var bytes = new byte[entry.Size];
                ReadFully( tarInputStream, bytes );
                await outputFileStream.WriteAsync(bytes, default);
            }
            catch (Exception ex)
            {
                throw new IOException($"Copy command failed: {ex.Message}");
            }

            using var streamReader = new StreamReader(stdError);
            while (streamReader.EndOfStream == false)
            {
                var error = await streamReader.ReadToEndAsync(default);
                Log.Error(error);
            }
        });

        // the kubectl uses also tar for copying files
        await _kubernetesClient.NamespacedPodExecAsync(
            selectedPod.Name,
            selectedNamespace.Name,
            selectedPod.Containers.First(),
            new[] { "sh", "-c", $"tar cf - {selectedFile.Name}" },
            false,
            handler,
            default);
    }
}