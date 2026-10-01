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
}
