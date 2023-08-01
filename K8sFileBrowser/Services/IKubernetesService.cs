// // Copyright (c) Vector Informatik GmbH. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using K8sFileBrowser.Models;

namespace K8sFileBrowser.Services;

public interface IKubernetesService
{
  IEnumerable<ClusterContext> GetClusterContexts();
  string GetCurrentContext();
  void SwitchClusterContext(ClusterContext clusterContext);
  IEnumerable<Namespace> GetNamespaces();
  IEnumerable<Pod> GetPods(string namespaceName);
  IList<FileInformation> GetFiles(string namespaceName, string podName, string containerName, string path);
  Task DownloadFile(Namespace? selectedNamespace, Pod? selectedPod, FileInformation selectedFile,
    string? saveFileName, CancellationToken cancellationToken = default);
  Task DownloadLog(Namespace? selectedNamespace, Pod? selectedPod,
      string? saveFileName, CancellationToken cancellationToken = default);
  
}