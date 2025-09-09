using System.Collections;
using System.Text;
using P1Monitor.Model;

namespace P1Monitor;

public record class UnitNumberMappings(string Unit, ObisMapping[] Mappings);

public class ObisMappingList(IReadOnlyList<ObisMapping> mappings) : IReadOnlyList<ObisMapping>
{
    private readonly TrieNode _root = new(mappings);

    public int Count { get; } = mappings.Count;

    public ObisMapping[] Tags { get; } = [.. mappings.Where(x => x.DsmrType == DsmrType.String || x.DsmrType == DsmrType.OnOff).OrderBy(x => x.FieldName)];

    public UnitNumberMappings[] NumberMappingsByUnit { get; } = [.. mappings.Where(x => x.DsmrType == DsmrType.Number).GroupBy(x => x.Unit).Select(g => new UnitNumberMappings(g.Key.ToString(), [.. g.OrderBy(x => x.FieldName)]))];

    public ObisMapping? TimeField { get; } = mappings.FirstOrDefault(x => x.DsmrType == DsmrType.Time);

    public ObisMapping this[int index] => mappings[index];

    public bool TryGetMappingById(ReadOnlySpan<byte> id, out ObisMapping? mapping)
    {
        mapping = _root.Find(id);
        return mapping != null;
    }

    public IEnumerator<ObisMapping> GetEnumerator()
    {
        return mappings.AsEnumerable().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return mappings.GetEnumerator();
    }

    private class TrieNode
    {
        private int _offset;
        private TrieNode[] _children = new TrieNode[256];
        private ObisMapping? _mapping;

        private TrieNode() { }

        public TrieNode(IReadOnlyList<ObisMapping> mappings)
        {
            foreach (ObisMapping mapping in mappings)
            {
                Add(Encoding.Latin1.GetBytes(mapping.Id), mapping);
            }

            // DFS to trim excess memory
            Stack<TrieNode> nodes = new();
            nodes.Push(this);
            while (nodes.TryPop(out TrieNode? node))
            {
                node.TrimExcessMemory();
                foreach (TrieNode child in node._children)
                {
                    if (child != null) nodes.Push(child);
                }
            }
        }

        private void Add(ReadOnlySpan<byte> span, ObisMapping mapping)
        {
            TrieNode node = this;
            for (int i = 0; i < span.Length; i++)
            {
                if (node._children[span[i]] == null)
                {
                    node = node._children[span[i]] = new TrieNode();
                }
                else
                {
                    node = node._children[span[i]];
                }
            }
            if (node._mapping != null) throw new ArgumentException("Duplicate id mapping");
            node._mapping = mapping;
        }

        private void TrimExcessMemory()
        {
            int i = 0;
            while (i < _children.Length && _children[i] == null) i++;

            if (i == _children.Length)
            {
                _children = [];
                return;
            }
            _offset = i;

            i = _children.Length - 1;
            while (i >= 0 && _children[i] == null) i--;

            TrieNode[] tmp = new TrieNode[i - _offset + 1];
            Array.Copy(_children, _offset, tmp, 0, tmp.Length);
            _children = tmp;
        }

        public ObisMapping? Find(ReadOnlySpan<byte> span)
        {
            TrieNode node = this;
            for (int i = 0; i < span.Length; i++)
            {
                int index = span[i] - node._offset;
                if (index < 0 || index >= node._children.Length || node._children[index] == null) return null;
                node = node._children[index];
            }
            return node._mapping;
        }
    }
}
