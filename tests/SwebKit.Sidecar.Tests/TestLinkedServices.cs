using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

internal static class TestLinkedServices
{
    /// <summary>A linked service with no configured roots — behaves as a pure pass-through.</summary>
    public static LinkedCollectionsService Empty() =>
        new(new LinkedCollectionRootRepository(), new LinkedCollectionFileService(new LinkedGitService()),
            new BrunoSyncService(), NullLogger<LinkedCollectionsService>.Instance);
}
