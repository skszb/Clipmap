using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public class TileCacheManager
{
    
    private List<TileCache> m_tileCaches;
    private List<int> m_baseTextureSizes;
    private List<int> m_tileSizes;

    private string m_path = "Cache/TextureTileCache";

    public void Initialize(MonoBehaviour owner, int[] baseTextureSize, int[] tileSize, string[] folderName, int[] capacity)
    {
        int depth = folderName.Length;
        m_tileCaches = new List<TileCache>(depth);
        m_baseTextureSizes = new List<int>(depth);
        m_tileSizes = new List<int>(depth);
        
        for (var i = 0; i < depth; ++i)
        {
            m_tileCaches.Add(new TileCache(owner, capacity[i], m_path + "/" + folderName[i]));
            m_baseTextureSizes.Add(baseTextureSize[i]);
            m_tileSizes.Add(tileSize[i]);
        }
    }

    // Return the texture tiles that the given region lies within. If a tile isn't cached yet, it will be null
    public List<(Texture2D textureTile, AABB2Int tileBound, AABB2Int croppedUpdateRegion)> GetTiles(
        AABB2Int updateRegion, int depth)
    {
        var ret = new List<(Texture2D textureTile, AABB2Int tileBound, AABB2Int croppedUpdateRegion)>();
        TileCache tileCache = m_tileCaches[depth];
        int halfTextureSize = m_baseTextureSizes[depth] / 2;
        // convert to texel space
        updateRegion += halfTextureSize;
        
        // Get the coordinates to the tiles that contains the given area
        int tileSize = m_tileSizes[depth];
        var bottomLeft = ClipmapUtil.SnapToGrid(updateRegion.min, tileSize);

        for (var coord = bottomLeft; coord.x < updateRegion.max.x; coord.x += tileSize)
        {
            for (coord.y = bottomLeft.y; coord.y < updateRegion.max.y; coord.y += tileSize)
            {
                Texture2D tile = tileCache.TryAcquireTile(coord);
                AABB2Int tileBound = new AABB2Int(coord.x, coord.y, coord.x + tileSize, coord.y + tileSize);
                AABB2Int croppedUpdateRegion = updateRegion.Clamp(tileBound);
                if (!croppedUpdateRegion.IsValid())
                {
                    continue;
                }

                // convert back to mip space of [-halfTextureSize, +halfTextureSize) for easier addressing in clipmap update
                ret.Add((tile, tileBound - halfTextureSize, croppedUpdateRegion - halfTextureSize));
            }
        }

        return ret;
    }

    public void LoadTiles(AABB2Int updateRegion, int depth)
    {
        TileCache tileCache = m_tileCaches[depth];
        int tileSize = m_tileSizes[depth];
        int textureSize = m_baseTextureSizes[depth];

        // convert to texel space
        updateRegion += textureSize / 2;
        updateRegion.Clamp(new AABB2Int(0, 0, textureSize, textureSize));

        var bottomLeft = ClipmapUtil.SnapToGrid(updateRegion.min, tileSize);

        for (var coord = bottomLeft; coord.x < updateRegion.max.x; coord.x += tileSize)
        {
            for (coord.y = bottomLeft.y; coord.y < updateRegion.max.y; coord.y += tileSize)
            {
                tileCache.LoadTile(coord, -depth);
            }
        }
    }
    
    internal class TileCache
    {
        private int m_capacity;

        private int m_vacantId; // TODO: Coroutine will make a hard copy!

        // Bidirectional map <Tile coordinates, LRUCacheInfo id>, for tiles already cached
        private Dictionary<Vector2Int, int> m_cacheLookupTable;
        private Vector2Int[] m_reverseCacheLookup;

        private HashSet<Vector2Int> m_loading; // Cache loading task that have been submitted

        private FLruCache m_lruInfoCache;
        Texture2D[] m_cachedTextures;

        private string m_path;
        private MonoBehaviour m_owner;

        public TileCache(MonoBehaviour owner, int capacity, string path)
        {
            this.m_cacheLookupTable = new Dictionary<Vector2Int, int>();
            this.m_reverseCacheLookup = new Vector2Int[capacity];
            this.m_loading = new HashSet<Vector2Int>();
            this.m_capacity = capacity;
            this.m_vacantId = capacity - 1;
            this.m_lruInfoCache = new FLruCache(capacity);
            this.m_cachedTextures = new Texture2D[capacity];
            this.m_path = path;
            this.m_owner = owner;
        }

        public Texture2D TryAcquireTile(Vector2Int tileCoordinates)
        {
            if (m_cacheLookupTable.TryGetValue(tileCoordinates, out int index))
            {
                if (m_lruInfoCache.SetActive(index))
                {
                    return m_cachedTextures[index];
                }
            }
            return null;
        }

        public void LoadTile(Vector2Int tileCoordinates, int priority = 0)
        {   
            // skip those already cached or being processed
            if (m_cacheLookupTable.ContainsKey(tileCoordinates))
            {
                return;
            }

            if (m_loading.Contains(tileCoordinates))
            {
                return;
            }

            m_loading.Add(tileCoordinates);
            m_owner.StartCoroutine(LoadTileAsync(tileCoordinates, priority));
        }

        private IEnumerator LoadTileAsync(Vector2Int coords, int priority)
        {
            string textureTilePath = m_path + $"/{coords.x}_{coords.y}";
            ResourceRequest request = Resources.LoadAsync<Texture2D>(textureTilePath);
            request.priority = priority;
            yield return request;
            
            // Loading finished
            m_loading.Remove(coords);

            if (request.asset is not Texture2D loadedTextureTile)
            {
                Debug.LogWarningFormat("Failed to load cache at path: {0}", textureTilePath);
                yield break;
            }
            if (!GetAvailableSlot(out int slot))
            {
                Debug.LogWarningFormat("Failed to get available slot in cache, got {0}.", slot);
                yield break;
            }
            
            // update lookupdable
            m_cacheLookupTable[coords] = slot;
            m_reverseCacheLookup[slot] = coords;
            m_cachedTextures[slot] = loadedTextureTile;
        }

        private bool GetAvailableSlot(out int slot)
        {
            if (m_vacantId < 0)
            {
                unsafe {
                    string order = "";
                    int headid = m_lruInfoCache.First;
                    for (var i = 0; i < m_capacity; ++i)
                    {
                        order += $"{m_lruInfoCache.nodeInfoList[headid].id} => ";
                        headid = m_lruInfoCache.nodeInfoList[headid].nextID;
                    }
                    Debug.Log($"Freeing head {m_lruInfoCache.First} \n new order: {order}");
                }
                
                // Cache full, need to free up space
                slot = m_lruInfoCache.First;
                Vector2Int oldTileCoord = m_reverseCacheLookup[slot];
                m_cacheLookupTable.Remove(oldTileCoord);
                m_reverseCacheLookup[slot] = new Vector2Int(int.MinValue, int.MinValue);
                m_cachedTextures[slot] = null;
            }
            else
            {
                slot = m_vacantId--;
            }
            
            return m_lruInfoCache.SetActive(slot);
        }
    }
    
    // LRU Cache implementation from GPUVT
    internal struct FNodeInfo
    {
        public int id;
        public int nextID;
        public int prevID;
    }
#if UNITY_EDITOR
    internal unsafe sealed class FLruCacheDebugView
    {
        FLruCache m_Target;

        public FLruCacheDebugView(FLruCache target)
        {
            m_Target = target;
        }

        public int Length
        {
            get
            {
                return m_Target.length;
            }
        }

        public FNodeInfo HeadNode
        {
            get
            {
                return m_Target.headNodeInfo;
            }
        }

        public FNodeInfo TailNode
        {
            get
            {
                return m_Target.tailNodeInfo;
            }
        }

        public List<FNodeInfo> NodeInfos
        {
            get
            {
                var result = new List<FNodeInfo>();
                for (int i = 0; i < m_Target.length; ++i)
                {
                    result.Add(m_Target.nodeInfoList[i]);
                }
                return result;
            }
        }
    }

    [DebuggerTypeProxy(typeof(FLruCacheDebugView))]
#endif
    internal unsafe struct FLruCache : IDisposable
    {
        internal int length;
        internal FNodeInfo headNodeInfo;
        internal FNodeInfo tailNodeInfo;
        [NativeDisableUnsafePtrRestriction]
        internal FNodeInfo* nodeInfoList;
        internal int First { get { return headNodeInfo.id; } }

        public FLruCache(in int length)
        {
            this.length = length;
            this.nodeInfoList = (FNodeInfo*)UnsafeUtility.Malloc(Marshal.SizeOf(typeof(FNodeInfo)) * length, 64, Allocator.Persistent);

            for (int i = 0; i < length; ++i)
            {
                nodeInfoList[i] = new FNodeInfo()
                {
                    id = i,
                };
            }
            for (int j = 0; j < length; ++j)
            {
                nodeInfoList[j].prevID = (j != 0) ? nodeInfoList[j - 1].id : 0;
                nodeInfoList[j].nextID = (j + 1 < length) ? nodeInfoList[j + 1].id : length - 1;
            }
            this.headNodeInfo = nodeInfoList[0];
            this.tailNodeInfo = nodeInfoList[length - 1];
        }

        public static void BuildLruCache(ref FLruCache lruCache, in int count)
        {
            lruCache.length = count;
            lruCache.nodeInfoList = (FNodeInfo*)UnsafeUtility.Malloc(Marshal.SizeOf(typeof(FNodeInfo)) * count, 64, Allocator.Persistent);


            for (int i = 0; i < count; ++i)
            {
                lruCache.nodeInfoList[i] = new FNodeInfo()
                {
                    id = i,
                };
            }
            for (int j = 0; j < count; ++j)
            {
                lruCache.nodeInfoList[j].prevID = (j != 0) ? lruCache.nodeInfoList[j - 1].id : 0;
                lruCache.nodeInfoList[j].nextID = (j + 1 < count) ? lruCache.nodeInfoList[j + 1].id : count - 1;
            }
            lruCache.headNodeInfo = lruCache.nodeInfoList[0];
            lruCache.tailNodeInfo = lruCache.nodeInfoList[count - 1];
        }

        public void Dispose()
        {
            UnsafeUtility.Free((void*)nodeInfoList, Allocator.Persistent);
        }

        public bool SetActive(in int id)
        {
            if (id < 0 || id >= length) { return false; }

            ref FNodeInfo nodeInfo = ref nodeInfoList[id];
            if (nodeInfo.id == tailNodeInfo.id) { return true; }
            Remove(ref nodeInfo);
            AddLast(ref nodeInfo);
            return true;
        }

        private void AddLast(ref FNodeInfo nodeInfo)
        {
            ref FNodeInfo lastNodeInfo = ref nodeInfoList[tailNodeInfo.id];
            tailNodeInfo = nodeInfo;

            lastNodeInfo.nextID = nodeInfo.id;
            nodeInfoList[lastNodeInfo.nextID] = nodeInfo;

            nodeInfo.prevID = lastNodeInfo.id;
            nodeInfoList[nodeInfo.prevID] = lastNodeInfo;
        }

        private void Remove(ref FNodeInfo nodeInfo)
        {
            if (headNodeInfo.id == nodeInfo.id)
            {
                headNodeInfo = nodeInfoList[nodeInfo.nextID];
            }
            else
            {
                ref FNodeInfo prevNodeInfo = ref nodeInfoList[nodeInfo.prevID];
                ref FNodeInfo nextNodeInfo = ref nodeInfoList[nodeInfo.nextID];
                prevNodeInfo.nextID = nodeInfo.nextID;
                nextNodeInfo.prevID = nodeInfo.prevID;
                // ZB: redundant logic below?
                nodeInfoList[prevNodeInfo.nextID] = nextNodeInfo;
                nodeInfoList[nextNodeInfo.prevID] = prevNodeInfo;
            }
        }
    }
}