using PassManager.Application.Common;
using PassManager.Domain.Entities;
using Xunit;

namespace PassManager.Application.Tests.Common;

public class FolderTreeTests
{
    [Fact]
    public void CollectAncestorChain_FolderIsRoot_ReturnsEmptyChain()
    {
        var rootId = Guid.NewGuid();
        var folders = new List<Folder>
        {
            new() { Id = rootId, ParentId = null, IsRoot = true }
        };

        var chain = FolderTree.CollectAncestorChain(folders, rootId);

        Assert.Empty(chain);
    }

    [Fact]
    public void CollectAncestorChain_NestedTwoLevelsDeep_ReturnsChainInTopToBottomOrder()
    {
        var rootId = Guid.NewGuid();
        var devId = Guid.NewGuid();
        var outilsId = Guid.NewGuid();
        var folders = new List<Folder>
        {
            new() { Id = rootId, ParentId = null, IsRoot = true },
            new() { Id = devId, ParentId = rootId, IsRoot = false, Name = "Dev" },
            new() { Id = outilsId, ParentId = devId, IsRoot = false, Name = "Outils" }
        };

        var chain = FolderTree.CollectAncestorChain(folders, outilsId);

        Assert.Equal(2, chain.Count);
        Assert.Equal("Dev", chain[0].Name);
        Assert.Equal("Outils", chain[1].Name);
    }

    [Fact]
    public void CollectAncestorChain_DirectChildOfRoot_ReturnsSingleElementChain()
    {
        var rootId = Guid.NewGuid();
        var devId = Guid.NewGuid();
        var folders = new List<Folder>
        {
            new() { Id = rootId, ParentId = null, IsRoot = true },
            new() { Id = devId, ParentId = rootId, IsRoot = false, Name = "Dev" }
        };

        var chain = FolderTree.CollectAncestorChain(folders, devId);

        var only = Assert.Single(chain);
        Assert.Equal("Dev", only.Name);
    }
}
