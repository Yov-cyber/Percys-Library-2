using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

namespace ComicReader.ContinuousReader
{
    // Thread-safe LRU cache minimal with optional TTL support.
    public class CacheManager<TKey, TValue>
        : IEnumerable<KeyValuePair<TKey, TValue>>
    {
        private readonly int _capacity;
        private readonly TimeSpan _ttl;
        private readonly object _sync = new object();

        // Internal cache node contains timestamp to support TTL-based expiry
        private class CacheNode
        {
            public TKey Key { get; set; }
            public TValue Value { get; set; }
            public DateTime InsertedUtc { get; set; }
            public CacheNode(TKey k, TValue v)
            {
                Key = k; Value = v; InsertedUtc = DateTime.UtcNow;
            }
        }

        private readonly Dictionary<TKey, LinkedListNode<CacheNode>> _map;
        private readonly LinkedList<CacheNode> _list;

        // Backwards-compatible constructor (no TTL)
        public CacheManager(int capacity = 500) : this(capacity, TimeSpan.Zero) { }

        // New ctor expected by tests: maxItems + ttl
        public CacheManager(int maxItems, TimeSpan ttl)
        {
            _capacity = Math.Max(1, maxItems);
            _ttl = ttl;
            _map = new Dictionary<TKey, LinkedListNode<CacheNode>>(_capacity);
            _list = new LinkedList<CacheNode>();
        }

        public int Count
        {
            get { lock (_sync) { return _map.Count; } }
        }

        // Compatibilidad: indexador similar a ConcurrentDictionary
        public TValue this[TKey key]
        {
            get
            {
                lock (_sync)
                {
                    if (_map.TryGetValue(key, out var node))
                    {
                        // check ttl
                        if (_ttl > TimeSpan.Zero && DateTime.UtcNow - node.Value.InsertedUtc > _ttl)
                        {
                            // expired
                            _map.Remove(key);
                            _list.Remove(node);
                            throw new KeyNotFoundException();
                        }
                        // mover a frente
                        _list.Remove(node);
                        _list.AddFirst(node);
                        return node.Value.Value;
                    }
                    throw new KeyNotFoundException();
                }
            }
            set
            {
                Set(key, value);
            }
        }

        // Compatibilidad: TryGetValue con la firma clásica
        public bool TryGetValue(TKey key, out TValue value)
        {
            return TryGet(key, out value);
        }

        // Compatibilidad: enumerar entradas (snapshot)
        public IEnumerable<KeyValuePair<TKey, TValue>> GetEntries()
        {
            lock (_sync)
            {
                // devolver snapshot para evitar enumeración concurrente
                return _list.Select(n => new KeyValuePair<TKey, TValue>(n.Key, n.Value)).ToList();
            }
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            // devolver snapshot enumerado
            return GetEntries().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEntries().GetEnumerator();
        }

        // Compatibilidad: Keys y Values enumerables (snapshots)
        public IEnumerable<TKey> Keys
        {
            get { lock (_sync) { return _map.Keys.ToList(); } }
        }

        public IEnumerable<TValue> Values
        {
            get { lock (_sync) { return _list.Select(n => n.Value).ToList(); } }
        }

        public void Set(TKey key, TValue value)
        {
            lock (_sync)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _list.Remove(node);
                }
                var cacheNode = new CacheNode(key, value);
                var newNode = new LinkedListNode<CacheNode>(cacheNode);
                _list.AddFirst(newNode);
                _map[key] = newNode;
                if (_map.Count > _capacity)
                {
                    var last = _list.Last;
                    if (last != null)
                    {
                        _map.Remove(last.Value.Key);
                        _list.RemoveLast();
                    }
                }
            }
        }

        public bool TryGet(TKey key, out TValue value)
        {
            lock (_sync)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    // check ttl
                    if (_ttl > TimeSpan.Zero && DateTime.UtcNow - node.Value.InsertedUtc > _ttl)
                    {
                        // expired
                        _map.Remove(key);
                        _list.Remove(node);
                        value = default(TValue)!;
                        return false;
                    }
                    // move to front
                    _list.Remove(node);
                    _list.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
                value = default(TValue)!;
                return false;
            }
        }

        public bool TryRemove(TKey key, out TValue value)
        {
            lock (_sync)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _map.Remove(key);
                    _list.Remove(node);
                    value = node.Value.Value;
                    return true;
                }
                value = default(TValue)!;
                return false;
            }
        }

        public bool ContainsKey(TKey key)
        {
            lock (_sync) { return _map.ContainsKey(key); }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _map.Clear();
                _list.Clear();
            }
        }
    }
}
