﻿using System;
using System.Collections.Generic;
using NitroSharp.NsScript;
using NitroSharp.Utilities;

#nullable enable

namespace NitroSharp.New
{
    internal sealed partial class World
    {
        public SmallList<Entity> Query(EntityQuery query)
        {
            if (EntityPath.IsValidPath(query, out EntityPath simplePath)
                && Get(simplePath) is Entity result)
            {
                return new SmallList<Entity>(result);
            }
            return QuerySlow(query);
        }

        private SmallList<Entity> QuerySlow(EntityQuery query)
        {
            var queryParts = query.EnumerateParts().ToSmallList();
            EntityQueryPart queryRoot = queryParts[0];
            SmallList<Entity> roots = default;
            if (queryRoot.IsWildcardPattern)
            {
                ReadOnlySpan<char> prefix = queryRoot.Value.Span[..^1];
                if (!queryRoot.SearchInAliases)
                {
                    foreach ((EntityId id, EntityRec rec) in _entities)
                    {
                        if (!rec.Entity.HasParent && id.Name.StartsWith(prefix))
                        {
                            roots.Add(rec.Entity);
                        }
                    }
                }
                else
                {
                    prefix = prefix[1..];
                    foreach ((EntityPath id, EntityId rec) in _aliases)
                    {
                        // TODO
                        Entity entity = Get(rec)!;
                        if (!entity.HasParent && id.Name.StartsWith(prefix))
                        {
                            roots.Add(Get(rec)!);
                        }
                    }
                }
            }
            else
            {
                Entity? root = Get(new EntityPath(queryRoot.Value.ToString()));
                if (root != null)
                {
                    roots.Add(root);
                }
            }

            SmallList<Entity> results = default;
            ReadOnlySpan<EntityQueryPart> remainingParts = queryParts.AsSpan()[1..];
            foreach (Entity root in roots.AsSpan())
            {
                Match(root, remainingParts, ref results);
            }

            return results;
        }

        private void Match(
            Entity entity,
            ReadOnlySpan<EntityQueryPart> remainingQueryParts,
            ref SmallList<Entity> results)
        {
            if (remainingQueryParts.Length == 0)
            {
                results.Add(entity);
                return;
            }

            EntityQueryPart part = remainingQueryParts[0];
            ReadOnlySpan<char> prefix = part.Value.Span;
            if (part.IsWildcardPattern)
            {
                prefix = prefix[..^1];
            }
            foreach (EntityId child in entity.Children)
            {
                if (child.Name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    // TODO
                    Match(Get(child)!, remainingQueryParts[1..], ref results);
                }
            }
        }
    }
}
