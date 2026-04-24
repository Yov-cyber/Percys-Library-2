using System;
using System.Collections.Generic;

namespace ComicReader.Utils
{
    public class SimpleLruCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey key, TValue value)>> _map;
        private readonly LinkedList<(TKey key, TValue value)> _list;

        public int Count => _map.Count;

        public SimpleLruCache(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>();
            _list = new LinkedList<(TKey, TValue)>();
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // mover al frente
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.value;
                return true;
            }
            value = default;
            return false;
        }

        public void AddOrUpdate(TKey key, TValue value)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                existing.Value = (key, value);
                _list.Remove(existing);
                _list.AddFirst(existing);
                return;
            }
            var node = new LinkedListNode<(TKey, TValue)>((key, value));
            _list.AddFirst(node);
            _map[key] = node;
            if (_map.Count > _capacity)
            {
                var last = _list.Last;
                if (last != null)
                {
                    _list.RemoveLast();
                    _map.Remove(last.Value.key);
                }
            }
        }

        public void Clear()
        {
            _map.Clear();
            _list.Clear();
        }
    }
}
