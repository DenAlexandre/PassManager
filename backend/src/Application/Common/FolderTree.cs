using PassManager.Domain.Entities;

namespace PassManager.Application.Common;

public static class FolderTree
{
    /// <summary>Returns rootId plus every folder transitively parented under it, given a flat list of candidate folders.</summary>
    public static HashSet<Guid> CollectDescendantIds(IEnumerable<Folder> candidateFolders, Guid rootId)
    {
        var childrenByParent = candidateFolders
            .Where(f => f.ParentId.HasValue)
            .GroupBy(f => f.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Id).ToList());

        var result = new HashSet<Guid> { rootId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenByParent.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (result.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the ancestor chain from just-below-root down to (and including) the folder with id
    /// <paramref name="folderId"/>, in top-to-bottom order. Excludes the true root folder. Empty if
    /// <paramref name="folderId"/> IS the root folder's own id.
    /// </summary>
    public static List<Folder> CollectAncestorChain(IEnumerable<Folder> candidateFolders, Guid folderId)
    {
        var byId = candidateFolders.ToDictionary(f => f.Id);
        var chain = new List<Folder>();
        var current = byId[folderId];

        while (!current.IsRoot)
        {
            chain.Add(current);
            if (current.ParentId is null)
            {
                break;
            }

            current = byId[current.ParentId.Value];
        }

        chain.Reverse();
        return chain;
    }
}
