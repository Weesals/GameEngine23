using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace FlecsCS {
    public enum EntityId : ulong {
        FLECS_HI_COMPONENT_ID = (256),
        EcsWorld = FLECS_HI_COMPONENT_ID + 0,
        EcsFlecs = FLECS_HI_COMPONENT_ID + 1,
        EcsFlecsCore = FLECS_HI_COMPONENT_ID + 2,
        EcsFlecsInternals = FLECS_HI_COMPONENT_ID + 3,
        EcsModule = FLECS_HI_COMPONENT_ID + 4,
        EcsPrivate = FLECS_HI_COMPONENT_ID + 5,
        EcsPrefab = FLECS_HI_COMPONENT_ID + 6,
        EcsDisabled = FLECS_HI_COMPONENT_ID + 7,

        EcsSlotOf = FLECS_HI_COMPONENT_ID + 8,
        EcsFlag = FLECS_HI_COMPONENT_ID + 9,

        /* Relationship properties */
        EcsWildcard = FLECS_HI_COMPONENT_ID + 10,
        EcsAny = FLECS_HI_COMPONENT_ID + 11,
        EcsThis = FLECS_HI_COMPONENT_ID + 12,
        EcsVariable = FLECS_HI_COMPONENT_ID + 13,
        EcsTransitive = FLECS_HI_COMPONENT_ID + 14,
        EcsReflexive = FLECS_HI_COMPONENT_ID + 15,
        EcsSymmetric = FLECS_HI_COMPONENT_ID + 16,
        EcsFinal = FLECS_HI_COMPONENT_ID + 17,
        EcsDontInherit = FLECS_HI_COMPONENT_ID + 18,
        EcsAlwaysOverride = FLECS_HI_COMPONENT_ID + 19,
        EcsTag = FLECS_HI_COMPONENT_ID + 20,
        EcsUnion = FLECS_HI_COMPONENT_ID + 21,
        EcsExclusive = FLECS_HI_COMPONENT_ID + 22,
        EcsAcyclic = FLECS_HI_COMPONENT_ID + 23,
        EcsTraversable = FLECS_HI_COMPONENT_ID + 24,
        EcsWith = FLECS_HI_COMPONENT_ID + 25,
        EcsOneOf = FLECS_HI_COMPONENT_ID + 26,

        /* Builtin relationships */
        EcsChildOf = FLECS_HI_COMPONENT_ID + 27,
        EcsIsA = FLECS_HI_COMPONENT_ID + 28,
        EcsDependsOn = FLECS_HI_COMPONENT_ID + 29,

        /* Identifier tags */
        EcsName = FLECS_HI_COMPONENT_ID + 30,
        EcsSymbol = FLECS_HI_COMPONENT_ID + 31,
        EcsAlias = FLECS_HI_COMPONENT_ID + 32,

        /* Events */
        EcsOnAdd = FLECS_HI_COMPONENT_ID + 33,
        EcsOnRemove = FLECS_HI_COMPONENT_ID + 34,
        EcsOnSet = FLECS_HI_COMPONENT_ID + 35,
        EcsUnSet = FLECS_HI_COMPONENT_ID + 36,
        EcsOnDelete = FLECS_HI_COMPONENT_ID + 37,
        EcsOnTableCreate = FLECS_HI_COMPONENT_ID + 38,
        EcsOnTableDelete = FLECS_HI_COMPONENT_ID + 39,
        EcsOnTableEmpty = FLECS_HI_COMPONENT_ID + 40,
        EcsOnTableFill = FLECS_HI_COMPONENT_ID + 41,
        EcsOnCreateTrigger = FLECS_HI_COMPONENT_ID + 42,
        EcsOnDeleteTrigger = FLECS_HI_COMPONENT_ID + 43,
        EcsOnDeleteObservable = FLECS_HI_COMPONENT_ID + 44,
        EcsOnComponentHooks = FLECS_HI_COMPONENT_ID + 45,
        EcsOnDeleteTarget = FLECS_HI_COMPONENT_ID + 46,
    };
    public enum WorldFlags : uint {
        QuitWorkers = (1u << 0),
        Readonly = (1u << 1),
        Init = (1u << 2),
        Quit = (1u << 3),
        Fini = (1u << 4),
        MeasureFrameTime = (1u << 5),
        MeasureSystemTime = (1u << 6),
        MultiThreaded = (1u << 7)
    };

    public enum EventFlags : uint {
        EcsEventTableOnly              = (1u << 4),   /* Table event (no data, same as iter flags) */
        EcsEventNoOnSet                = (1u << 16),  /* Don't emit OnSet/UnSet for inherited ids */
    }

    public enum RecordId : uint {
        EcsIdOnDeleteRemove         = (1u << 0),
        EcsIdOnDeleteDelete         = (1u << 1),
        EcsIdOnDeletePanic          = (1u << 2),
        EcsIdOnDeleteMask           = (EcsIdOnDeletePanic|EcsIdOnDeleteRemove|EcsIdOnDeleteDelete),

        EcsIdOnDeleteObjectRemove   = (1u << 3) ,
        EcsIdOnDeleteObjectDelete   = (1u << 4) ,
        EcsIdOnDeleteObjectPanic    = (1u << 5) ,
        EcsIdOnDeleteObjectMask     = (EcsIdOnDeleteObjectPanic|EcsIdOnDeleteObjectRemove|EcsIdOnDeleteObjectDelete),

    }
    public enum RecordFlags : uint {
        EcsIdExclusive              = (1u << 6) ,
        EcsIdDontInherit            = (1u << 7) ,
        EcsIdTraversable            = (1u << 8) ,
        EcsIdTag                    = (1u << 9) ,
        EcsIdWith                   = (1u << 10),
        EcsIdUnion                  = (1u << 11),
        EcsIdAlwaysOverride         = (1u << 12),
                              
        EcsIdHasOnAdd               = (1u << 16), /* Same values as table flags */
        EcsIdHasOnRemove            = (1u << 17), 
        EcsIdHasOnSet               = (1u << 18),
        EcsIdHasUnSet               = (1u << 19),
        EcsIdHasOnTableFill         = (1u << 20),
        EcsIdHasOnTableEmpty        = (1u << 21),
        EcsIdHasOnTableCreate       = (1u << 22),
        EcsIdHasOnTableDelete       = (1u << 23),
        EcsIdEventMask              = (
            EcsIdHasOnAdd|EcsIdHasOnRemove|EcsIdHasOnSet|EcsIdHasUnSet|
            EcsIdHasOnTableFill|EcsIdHasOnTableEmpty|EcsIdHasOnTableCreate|
            EcsIdHasOnTableDelete)
    }
    public enum TableFlags : uint {
        /* Composite table flags */
        EcsTableHasBuiltins            = (1u << 1),  /* Does table have builtin components */
        EcsTableIsPrefab               = (1u << 2),  /* Does the table store prefabs */
        EcsTableHasIsA                 = (1u << 3),  /* Does the table have IsA relationship */
        EcsTableHasChildOf             = (1u << 4),  /* Does the table type ChildOf relationship */
        EcsTableHasName                = (1u << 5),  /* Does the table type have (Identifier, Name) */
        EcsTableHasPairs               = (1u << 6),  /* Does the table type have pairs */
        EcsTableHasModule              = (1u << 7),  /* Does the table have module data */
        EcsTableIsDisabled             = (1u << 8),  /* Does the table type has EcsDisabled */
        EcsTableHasCtors               = (1u << 9),
        EcsTableHasDtors               = (1u << 10),
        EcsTableHasCopy                = (1u << 11),
        EcsTableHasMove                = (1u << 12),
        EcsTableHasUnion               = (1u << 13),
        EcsTableHasToggle              = (1u << 14),
        EcsTableHasOverrides           = (1u << 15),

        EcsTableHasOnAdd              = (1u << 16), /* Same values as id flags */
        EcsTableHasOnRemove            = (1u << 17),
        EcsTableHasOnSet               = (1u << 18),
        EcsTableHasUnSet               = (1u << 19),
        EcsTableHasOnTableFill         = (1u << 20),
        EcsTableHasOnTableEmpty        = (1u << 21),
        EcsTableHasOnTableCreate       = (1u << 22),
        EcsTableHasOnTableDelete       = (1u << 23),

        EcsTableHasTraversable         = (1u << 25),
        EcsTableHasTarget              = (1u << 26),

        EcsTableMar,kedForDelete     = (1u << 30),
        EcsTableHasLifecycle        = (EcsTableHasCtors | EcsTableHasDtors),
        EcsTableIsComplex           = (EcsTableHasLifecycle | EcsTableHasUnion | EcsTableHasToggle),
        EcsTableHasAddActions       = (EcsTableHasIsA | EcsTableHasUnion | EcsTableHasCtors | EcsTableHasOnAdd | EcsTableHasOnSet),
        EcsTableHasRemoveActions    = (EcsTableHasIsA | EcsTableHasDtors | EcsTableHasOnRemove | EcsTableHasUnSet),
    }

    /** Record for entity index */
    public class ecs_record_t {
        public const uint ECS_ROW_MASK                  = (0x0FFFFFFFu);
        public const uint ECS_ROW_FLAGS_MASK            = (~ECS_ROW_MASK);
        public static int ECS_RECORD_TO_ROW(uint v)          => (int)((uint)v & ECS_ROW_MASK);
        public static uint ECS_RECORD_TO_ROW_FLAGS(uint v)    => ((uint)v & ECS_ROW_FLAGS_MASK);
        public static uint ECS_ROW_TO_RECORD(int row, uint flags) => ((uint)((uint)row) | (flags));

        public IdRecord idr; /* Id record to (*, entity) for target entities */
        public ecs_table_t table;   /* Identifies a type (and table) in world */
        public uint row;         /* Table row of the entity */
        public int dense;        /* Index in dense array */
        public bool IsValid => table != null;
    };
    /** Stage-specific component data */
    public struct ecs_data_t {
        public List<ulong> entities;              /* Entity identifiers */
        public List<ecs_record_t> records;               /* Ptrs to records in main entity index */
        public List<byte> columns;              /* Component columns */
    };

    /* Payload for id cache */
    public struct ecs_table_cache_t {
        public Dictionary<ulong, ecs_table_record_t> index; /* <table_id, T*> */
        //ecs_table_cache_list_t tables;
        //ecs_table_cache_list_t empty_tables;
        public ecs_table_record_t Get(ecs_table_t table) {
            if (index == null) return default;
            if (index.TryGetValue(table.id, out var value)) return value;
            return default;
        }
    }
    public class ecs_table_cache_hdr_t {
        public ecs_table_cache_t cache;
        public ecs_table_t table;
        public ecs_table_cache_hdr_t prev, next;
        public bool empty;
    }
    public struct ecs_table_record_t {
        public ecs_table_cache_hdr_t hdr;  /* Table cache header */
        public int column;             /* First column where id occurs in table */
        public int count;              /* Number of times id occurs in table */
    };

    public class ecs_switch_t {
        public class ecs_switch_header_t {
            public int element;    /* First element for value */
            public int count;      /* Number of elements for value */
            public static ecs_switch_header_t Null;
        }
        public struct ecs_switch_node_t : IEquatable<ecs_switch_node_t> {
            public int next;       /* Next node in list */
            public int prev;       /* Prev node in list */
            public bool Equals(ecs_switch_node_t other) {
                return next == other.next && prev == other.prev;
            }
        }
        public Dictionary<ulong, ecs_switch_header_t> hdrs;     /* map<uint64_t, ecs_switch_header_t> */
        public List<ecs_switch_node_t> nodes;    /* vec<ecs_switch_node_t> */
        public List<ulong> values;   /* vec<uint64_t> */

        ecs_switch_header_t flecs_switch_get_header(ulong value)
        {
            if (value == 0) {
                return ecs_switch_header_t.Null;
            }
            return hdrs[value];
        }

        ecs_switch_header_t flecs_switch_ensure_header(ulong value) {
            var node = flecs_switch_get_header(value);
            if (node == null && (value != 0)) {
                node = new() {
                    count = 0,
                    element = -1,
                };
                hdrs[value] = node;
            }
            return node;
        }
        void flecs_switch_remove_node(ecs_switch_header_t hdr, List<ecs_switch_node_t> nodes, ecs_switch_node_t node, int element) {
            Debug.Assert(nodes[element].Equals(node));

            /* Update previous node/header */
            if (hdr.element == element) {
                Debug.Assert(node.prev == -1);
                /* If this is the first node, update the header */
                hdr.element = node.next;
            } else {
                /* If this is not the first node, update the previous node to the 
                 * removed node's next ptr */
                Debug.Assert(node.prev != -1);
                ref var prev_node = ref CollectionsMarshal.AsSpan(nodes)[node.prev];
                prev_node.next = node.next;
            }

            /* Update next node */
            var next = node.next;
            if (next != -1) {
                Debug.Assert(next >= 0);
                /* If this is not the last node, update the next node to point to the
                 * removed node's prev ptr */
                var next_node = CollectionsMarshal.AsSpan(nodes)[next];
                next_node.prev = node.prev;
            }

            /* Decrease count of current header */
            hdr.count--;
            Debug.Assert(hdr.count >= 0);
        }

        public void flecs_switch_add() {
            nodes.Add(new ecs_switch_node_t() {
                next = -1,
                prev = -1,
            });
            values.Add(0);
        }
        public void flecs_switch_set_count(int count) {
            var old_count = nodes.Count;
            if (old_count == count) return;

            while (count < nodes.Count) nodes.Add(default);
            while (count < values.Count) values.Add(default);
            while (count > nodes.Count) nodes.RemoveAt(nodes.Count - 1);
            while (count > values.Count) values.RemoveAt(values.Count - 1);

            for (var i = old_count; i < count; i++) {
                nodes[i] = new ecs_switch_node_t() { next = -1, prev = -1, };
                values[i] = 0;
            }
        }
        public int flecs_switch_count() {
            Debug.Assert(values.Count == nodes.Count);
            return values.Count;
        }
        public void flecs_switch_set(int element, ulong value) {
            Debug.Assert(element < nodes.Count);
            Debug.Assert(element < values.Count);
            Debug.Assert(element >= 0);

            var cur_value = values[element];

            /* If the node is already assigned to the value, nothing to be done */
            if (cur_value == value) return;

            ref var node = ref CollectionsMarshal.AsSpan(nodes)[element];

            var dst_hdr = flecs_switch_ensure_header(value);
            var cur_hdr = flecs_switch_get_header(cur_value);

            /* If value is not 0, and dst_hdr is NULL, then this is not a valid value
             * for this switch */
            Debug.Assert(dst_hdr != null || value == default);

            if (cur_hdr != null) {
                flecs_switch_remove_node(cur_hdr, nodes, node, element);
            }

            /* Now update the node itself by adding it as the first node of dst */
            node.prev = -1;
            values[element] = value;

            if (dst_hdr != null) {
                node.next = dst_hdr.element;

                /* Also update the dst header */
                var first = dst_hdr.element;
                if (first != -1) {
                    Debug.Assert(first >= 0);
                    ref var first_node = ref CollectionsMarshal.AsSpan(nodes)[first];
                    first_node.prev = element;
                }

                dst_hdr.element = element;
                dst_hdr.count++;
            }
        }

        public void flecs_switch_remove(int elem) {
            Debug.Assert(elem < nodes.Count);
            Debug.Assert(elem >= 0);

            var value = values[elem];
            var node = nodes[elem];

            /* If node is currently assigned to a case, remove it from the list */
            if (value != 0) {
                var hdr = flecs_switch_get_header(value);
                Debug.Assert(hdr != null);

                flecs_switch_remove_node(hdr, nodes, node, elem);
            }

            var last_elem = nodes.Count - 1;
            if (last_elem != elem) {
                var last = nodes[^1];
                int next = last.next, prev = last.prev;
                if (next != -1) {
                    ref var n = ref CollectionsMarshal.AsSpan(nodes)[next];
                    n.prev = elem;
                }

                if (prev != -1) {
                    ref var n = ref CollectionsMarshal.AsSpan(nodes)[prev];
                    n.next = elem;
                } else {
                    var hdr = flecs_switch_get_header(values[last_elem]);
                    if (hdr != null && hdr.element != -1) {
                        Debug.Assert(hdr.element == last_elem);
                        hdr.element = elem;
                    }
                }
            }

            /* Remove element from arrays */
            nodes.RemoveAt(elem);
            values.RemoveAt(elem);
        }

        public ulong flecs_switch_get(int element) {
            Debug.Assert(element<values.Count);
            Debug.Assert(element >= 0);
            return values[element];
        }
    }
    public class ecs_bitset_t {
        private ulong[] data = Array.Empty<ulong>();
        public int Count { get; private set; }
        public int Size => data.Length;
        public ecs_bitset_t() { }
        public void EnsureInternal(int newSize) {
            newSize = ((newSize - 1) / 64 + 1) * 64;
            Array.Resize(ref data, newSize);
        }
        public void Ensure(int newCount) {
            if (newCount <= Count) return;
            Count = newCount;
            EnsureInternal(newCount);
        }
        public void AddN(int addCount) {
            EnsureInternal(Count += addCount);
        }
        public void Set(int elem, bool value) {
            Debug.Assert(elem < Count);
            uint hi = (uint)(elem >> 6);
            uint lo = (uint)(elem & 0x03F);
            ref var v = ref data[hi];
            v = (v & ~(1ul << (int)lo)) | ((value ? 1ul : 0ul) << (int)lo);
        }
        public bool Get(int elem) {
            Debug.Assert(elem < Count);
            return ((data[elem >> 6]) & (1u << (elem & 0x3f))) != 0;
        }
        public void Remove(int elem) {
            Debug.Assert(elem < Count);
            var last = Count - 1;
            var last_value = Get(last);
            Set(elem, last_value);
            Set(last, false);
            --Count;
        }
        public void Swap(int elem_a, int elem_b) {
            Debug.Assert(elem_a < Count && elem_b < Count);
            var a = Get(elem_a);
            var b = Get(elem_b);
            Set(elem_a, b);
            Set(elem_b, a);
        }
        public void flecs_bitset_addn(int addCount) {
            Count += addCount;
            Ensure(Count);
        }
    }

    /** Infrequently accessed data not stored inline in ecs_table_t */
    public class ecs_table__t {
        public ulong hash;                   /* Type hash */
        public int lockref;                    /* Prevents modifications */
        public int refcount;                /* Increased when used as storage table */
        public int traversable_count;       /* Number of observed entities in table */
        public ushort generation;             /* Used for table cleanup */
        public ushort record_count;           /* Table record count including wildcards */

        public ecs_table_record_t records; /* Array with table records */
        public Dictionary<string, int> name_index;       /* Cached pointer to name index */

        public List<ecs_switch_t> sw_columns;        /* Switch columns */
        public List<ecs_bitset_t> bs_columns;        /* Bitset columns */
        public ushort sw_count;
        public ushort sw_offset;
        public ushort bs_count;
        public ushort bs_offset;
        public ushort ft_offset;
    }

    /** A table is the Flecs equivalent of an archetype. Tables store all entities
     * with a specific set of components. Tables are automatically created when an
     * entity has a set of components not previously observed before. When a new
     * table is created, it is automatically matched with existing queries */
    public class ecs_table_t {
        public ulong id;                     /* Table id in sparse set */
        public TableFlags flags;             /* Flags for testing table properties */
        public ushort storage_count;          /* Number of components (excluding tags) */
        public ulong[] type;                 /* Identifies table type in type_index */

        public ecs_graph_node_t node;           /* Graph node */
        public ecs_data_t data;                 /* Component storage */
        public TypeInfo[] type_info;     /* Cached type info */
        public int[] dirty_state;            /* Keep track of changes in columns */
        
        public ecs_table_t storage_table;      /* Table without tags */
        public ulong[] storage_ids;           /* Component ids (prevent indirection) */
        public int[] storage_map;            /* Map type <-> data type
                                                 *  - 0..count(T):        type -> data_type
                                                 *  - count(T)..count(S): data_type -> type
                                                 */

        public ecs_table__t _;                 /* Infrequently accessed table metadata */

        public int ecs_table_count() {
            return data.entities.Count;
        }
        /* Append entity to table */
        public int flecs_table_append(ulong entity, ecs_record_t record, bool construct, bool on_add) {
            Debug.Assert(_.lockref == 0);
            Debug.Assert((flags & TableFlags.EcsTableHasTarget) != 0);

            /* Get count & size before growing entities array. This tells us whether the
             * arrays will realloc */
            var count = data.entities.Count;
            var column_count = storage_count;
            var columns = data.columns;

            /* Grow buffer with entity ids, set new element to new entity */
            data.entities.Add(entity);

            /* Add record ptr to array with record ptrs */
            data.records.Add(record);

            /* If the table is monitored indicate that there has been a change */
            flecs_table_mark_table_dirty(0);
            Debug.Assert(count >= 0);

            /* Fast path: no switch columns, no lifecycle actions */
            if ((flags & TableFlags.EcsTableIsComplex) == 0) {
                flecs_table_fast_append(type_info, columns, column_count);
                if (count == 0) {
                    throw new NotImplementedException();
                    //flecs_table_set_empty(world, table); /* See below */
                }
                return count;
            }

            var entities = data.entities;

            /* Reobtain size to ensure that the columns have the same size as the 
             * entities and record vectors. This keeps reasoning about when allocations
             * occur easier. */
            var size = data.entities.Count;

            flecs_table_fast_append(type_info, columns, column_count);
            /* Grow component arrays with 1 element */
            for (var i = 0; i < column_count; i++) {
                var column = columns[i];
                var ti = type_info[i];

                /*ecs_iter_action_t on_add_hook;
                if (on_add && (on_add_hook = ti->hooks.on_add)) {
                    flecs_table_invoke_hook(world, table, on_add_hook, EcsOnAdd, column,
                        &entities[count], table->storage_ids[i], count, 1, ti);
                }*/
            }

            var meta = _;
            var sw_count = meta.sw_count;
            var bs_count = meta.bs_count;
            var sw_columns = meta.sw_columns;
            var bs_columns = meta.bs_columns;

            /* Add element to each switch column */
            for (var i = 0; i < sw_count; i++) {
                Debug.Assert(sw_columns != null);
                var sw = sw_columns[i];
                sw.flecs_switch_add();
            }

            /* Add element to each bitset column */
            for (var i = 0; i < bs_count; i++) {
                Debug.Assert(bs_columns != null);
                var bs = bs_columns[i];
                bs.flecs_bitset_addn(1);
            }

            /* If this is the first entity in this table, signal queries so that the
             * table moves from an inactive table to an active table. */
            if (count == 0) {
                flecs_table_set_empty();
            }

            return count;
        }
        public void flecs_table_set_empty() {
            if (ecs_table_count() != 0) {
                _.generation = 0;
            }

            throw new NotImplementedException();
            //flecs_sparse_ensure_fast_t(world->pending_tables, ecs_table_t *, (uint32_t)table->id)[0] = table;
        }
        private void flecs_table_mark_table_dirty(int index) {
            if (dirty_state != null) dirty_state[index]++;
        }
        /* Append operation for tables that don't have any complex logic */
        private void flecs_table_fast_append(Span<TypeInfo> type_info, List<byte> columns, int count) {
            /* Add elements to each column array */
            for (var i = 0; i < count; i++) {
                var ti = type_info[i];
                for (int j = 0; j < ti.Size; ++j) columns.Add(default);
            }
        }
    };

    public class EntityPage {     //ecs_entity_index_page_t
        public struct EcsRecord {  //ecs_record_t
            public IdRecord IdR; /* Id record to (*, entity) for target entities */
            public ecs_table_t Table;   /* Identifies a type (and table) in world */
            public uint Row;         /* Table row of the entity */
            public int Dense;        /* Index in dense array */
            public bool IsValid => Dense > 0;
            public static EcsRecord Null;
        }
        public const int FLECS_ENTITY_PAGE_BITS = (12);
        public const int FLECS_ENTITY_PAGE_SIZE = (1 << FLECS_ENTITY_PAGE_BITS);
        public const uint FLECS_ENTITY_PAGE_MASK = ((uint)FLECS_ENTITY_PAGE_SIZE - 1);

        [System.Runtime.CompilerServices.InlineArray(FLECS_ENTITY_PAGE_SIZE)]
        public struct RecordSet {
            public EcsRecord Record;
        }
        public RecordSet Records;
    }

    public class EntityIndex {    //ecs_entity_index_t
        public const ulong ECS_ID_FLAGS_MASK = (0xFFul << 60);
        public const ulong ECS_ENTITY_MASK = (0xFFFFFFFFul);
        public const ulong ECS_GENERATION_MASK = (0xFFFFul << 32);
        public static ulong ECS_GENERATION(ulong e) => ((e & ECS_GENERATION_MASK) >> 32);
        public static ulong ECS_GENERATION_INC(ulong e) => ((e & ~ECS_GENERATION_MASK) | ((0xFFFF & (ECS_GENERATION(e) + 1)) << 32));
        public const ulong ECS_COMPONENT_MASK = (~ECS_ID_FLAGS_MASK);
        public static bool ECS_HAS_ID_FLAG(ulong e, ulong flag) => ((e) & flag) != 0;
        public static bool ECS_IS_PAIR(ulong id) => (((id) & IdRecord.ECS_ID_FLAGS_MASK) == IdRecord.ECS_PAIR);
        public static ulong ECS_PAIR_FIRST(ulong e) => (IdRecord.ecs_entity_t_hi(e & ECS_COMPONENT_MASK));
        public static ulong ECS_PAIR_SECOND(ulong e) => (IdRecord.ecs_entity_t_lo(e));
        public static bool ECS_HAS_RELATION(ulong e, ulong rel) => (ECS_HAS_ID_FLAG(e, IdRecord.ECS_PAIR) && (ECS_PAIR_FIRST(e) == rel));

        public List<ulong> Dense = new();
        public List<EntityPage> Pages = new();
        public int AliveCount;
        public ulong MaxId;
        public EntityIndex() {
            Dense.Add(0);
        }
        EntityPage EnsurePage(uint id) {    //flecs_entity_index_ensure_page
            var page_index = (int)(id >> EntityPage.FLECS_ENTITY_PAGE_BITS);
            while (page_index >= Pages.Count) Pages.Add(default);
            if (Pages[page_index] == null) Pages[page_index] = new();
            return Pages[page_index];
        }
        public ref EntityPage.EcsRecord GetAny(ulong entity) {  //flecs_entity_index_get_any
            uint id = (uint)entity;
            int page_index = (int)(id >> EntityPage.FLECS_ENTITY_PAGE_BITS);
            var page = Pages[page_index];
            return ref page.Records[(int)(id & EntityPage.FLECS_ENTITY_PAGE_MASK)];
        }
        public ref EntityPage.EcsRecord Get(ulong entity) {  //flecs_entity_index_get
            ref var r = ref GetAny(entity);
            Debug.Assert(r.Dense < AliveCount);
            Debug.Assert(Dense[r.Dense] == entity);
            return ref r;
        }
        public ref EntityPage.EcsRecord TryGetAny(ulong entity) {   //flecs_entity_index_try_get_any
            uint id = (uint)entity;
            int page_index = (int)(id >> EntityPage.FLECS_ENTITY_PAGE_BITS);
            if (page_index >= Pages.Count) return ref EntityPage.EcsRecord.Null;
            var page = Pages[page_index];
            if (page == null) return ref EntityPage.EcsRecord.Null;
            ref var r = ref page.Records[(int)(id & EntityPage.FLECS_ENTITY_PAGE_MASK)];
            if (r.Dense == 0) return ref EntityPage.EcsRecord.Null;
            return ref r;
        }
        public ref EntityPage.EcsRecord TryGet(ulong entity) {  //flecs_entity_index_try_get
            ref var r = ref TryGetAny(entity);
            if (!r.IsValid) return ref EntityPage.EcsRecord.Null;
            if (r.Dense >= AliveCount) return ref EntityPage.EcsRecord.Null;
            if (Dense[r.Dense] != entity) return ref EntityPage.EcsRecord.Null;
            return ref r;
        }
        public ref EntityPage.EcsRecord Ensure(ulong entity) {  //flecs_entity_index_ensure
            var id = (uint)entity;
            var page = EnsurePage(id);
            ref var r = ref page.Records[(int)(id & EntityPage.FLECS_ENTITY_PAGE_MASK)];

            var dense = r.Dense;
            if (dense != 0) {
                /* Entity is already alive, nothing to be done */
                if (dense < AliveCount) {
                    Debug.Assert(Dense[dense] == entity);
                    return ref r;
                }
            } else {
                /* Entity doesn't have a dense index yet */
                Dense.Add(entity);
                r.Dense = dense = Dense.Count - 1;
                MaxId = id > MaxId ? id : MaxId;
            }

            Debug.Assert(dense != 0);

            /* Entity is not alive, swap with first not alive element */
            var e_swap = Dense[AliveCount];
            ref var r_swap = ref GetAny(e_swap);
            Debug.Assert(r_swap.Dense == AliveCount);

            r_swap.Dense = dense;
            r.Dense = AliveCount;
            Dense[dense] = e_swap;
            Dense[AliveCount++] = entity;

            Debug.Assert(IsAlive(entity));

            return ref r;
        }
        public void Remove(ulong entity) {     //flecs_entity_index_remove
            ref var r = ref TryGet(entity);
            /* Entity is not alive or doesn't exist, nothing to be done */
            if (!r.IsValid) return;

            var dense = r.Dense;
            var i_swap = --AliveCount;
            var e_swap = Dense[i_swap];
            ref var r_swap = ref GetAny(e_swap);
            Debug.Assert(r_swap.Dense == i_swap);

            r_swap.Dense = dense;
            r.Table = default;
            r.IdR = default;
            r.Row = 0;
            r.Dense = i_swap;
            Dense[dense] = e_swap;
            Dense[i_swap] = ECS_GENERATION_INC(entity);
            Debug.Assert(!IsAlive(entity));
        }
        public void SetGeneration(ulong entity) {  //flecs_entity_index_set_generation
            ref var r = ref TryGetAny(entity);
            if (r.IsValid) {
                Dense[r.Dense] = entity;
            }
        }
        public ulong GetGeneration(ulong entity) {  //flecs_entity_index_get_generation
            ref var r = ref TryGetAny(entity);
            if (r.IsValid) return Dense[r.Dense];
            return 0;
        }
        public bool IsAlive(ulong entity) {    //flecs_entity_index_is_alive
            return TryGet(entity).IsValid;
        }
        public bool IsValid(ulong entity) {    //flecs_entity_index_is_valid
            var id = (uint)entity;
            ref var r = ref TryGetAny(id);
            /* Doesn't exist yet, so is valid */
            if (!r.IsValid) return true;

            /* If the id exists, it must be alive */
            return r.Dense < AliveCount;
        }
        public bool Exists(ulong entity) {  //flecs_entity_index_exists
            return TryGetAny(entity).IsValid;
        }
        public ulong NewId() {      //flecs_entity_index_new_id
            /* Recycle id */
            if (AliveCount != Dense.Count) return Dense[AliveCount++];

            /* Create new id */
            uint id = (uint)++MaxId;
            Dense.Add(id);

            var page = EnsurePage(id);
            ref var r = ref page.Records[(int)(id & EntityPage.FLECS_ENTITY_PAGE_MASK)];
            r.Dense = AliveCount++;
            Debug.Assert(AliveCount == Dense.Count);
            return id;
        }
        public Span<ulong> NewIds(int count) {     //flecs_entity_index_new_ids
            var alive_count = AliveCount;
            var new_count = alive_count + count;
            var dense_count = Dense.Count;

            if (new_count < dense_count) {
                /* Recycle ids */
                AliveCount = new_count;
                return CollectionsMarshal.AsSpan(Dense).Slice(alive_count);
            }

            /* Allocate new ids */
            while (Dense.Count < new_count) Dense.Add(default);
            var to_add = new_count - dense_count;
            for (var i = 0; i < to_add; i++) {
                var id = (uint)++MaxId;
                var dense = dense_count + i;
                Dense[dense] = id;
                var page = EnsurePage(id);
                ref var r = ref page.Records[(int)(id & EntityPage.FLECS_ENTITY_PAGE_MASK)];
                r.Dense = dense;
            }

            AliveCount = new_count;
            return CollectionsMarshal.AsSpan(Dense).Slice(AliveCount);
        }
        public void SetSize(int size) {     //flecs_entity_index_set_size
            while (Dense.Count < size) Dense.Add(default);
        }
        public int Count() { return AliveCount - 1; }   //flecs_entity_index_count
        public int Size() { return Dense.Count - 1; }   //flecs_entity_index_size
        public int NotAliveCount() { return Dense.Count - AliveCount; } //flecs_entity_index_not_alive_count
        public void Clear() {
            var count = Pages.Count;
            for (var i = 0; i < count; i++) {
                if (Pages[i] != null) Pages[i] = default;
            }

            SetSize(1);

            AliveCount = 1;
            MaxId = 0;
        }
        public Span<ulong> IndexIds() {     //flecs_entity_index_ids
            return CollectionsMarshal.AsSpan(Dense).Slice(1);
        }
        private static void CopyInternal(EntityIndex dst, EntityIndex src) {        //flecs_entity_index_copy_intern
            dst.SetSize(src.Size());
            var count = src.AliveCount;
            for (var i = 0; i < count - 1; i++) {
                var id = src.Dense[i];
                ref var src_ptr = ref src.Get(id);
                ref var dst_ptr = ref dst.Ensure(id);
                dst.SetGeneration(id);
                dst_ptr = src_ptr;
            }

            dst.MaxId = src.MaxId;
            Debug.Assert(src.AliveCount == dst.AliveCount);
        }

        public void Copy(EntityIndex dst, EntityIndex src) {       //flecs_entity_index_copy
            if (src == null) return;
            CopyInternal(dst, src);
        }

        public void Restore(EntityIndex dst, EntityIndex src) {     //flecs_entity_index_restore
            if (src == null) return;
            dst.Clear();
            CopyInternal(dst, src);
        }
    }
    public class NameIndex {
        public static ulong Hash(Span<byte> ptr) {     //flecs_name_index_hash
            ulong hash = 0;
            foreach (var item in ptr) hash = (hash * 53) ^ (hash >> 4) + item;
            return hash;
        }
        public int Compare(Span<byte> ptr1, Span<byte> ptr2) {      //flecs_name_index_compare
            int lenDelta = ptr1.Length - ptr2.Length;
            if (lenDelta != 0) return lenDelta < 0 ? -1 : 1;
            return ptr1.SequenceCompareTo(ptr2);
        }

    }
    ref struct ecs_event_desc_t {
        /** The event id. Only triggers for the specified event will be notified */
        public EntityId eventType;

        /** Component ids. Only triggers with a matching component id will be
         * notified. Observers are guaranteed to get notified once, even if they
         * match more than one id. */
        public ecs_type_t ids;

        /** The table for which to notify. */
        public ecs_table_t table;

        /** Optional 2nd table to notify. This can be used to communicate the
         * previous or next table, in case an entity is moved between tables. */
        public ecs_table_t other_table;

        /** Limit notified entities to ones starting from offset (row) in table */
        public int offset;

        /** Limit number of notified entities to count. offset+count must be less
         * than the total number of entities in the table. If left to 0, it will be
         * automatically determined by doing ecs_table_count(table) - offset. */
        public int count;

        /** Single-entity alternative to setting table / offset / count */
        public ulong entity;

        /** Optional context. Assigned to iter param member */
        public Span<byte> param;

        /** Observable (usually the world) */
        public object observable;

        /** Event flags */
        public EventFlags flags;
    } 

    public class TypeInfo {
        public Type Type;
        public int Size;

        public static TypeInfo Get(ulong rel) {
            throw new NotImplementedException();
        }
    }
    public class ecs_type_t : List<ulong> {
    }
    public struct ecs_table_diff_t {
        public ecs_type_t added;                /* Components added between tables */
        public ecs_type_t removed;              /* Components removed between tables */
    }
    
    public struct RecordElem {
        public IdRecord Previous;
        public IdRecord Next;
    }
    public class IdRecord {     //ecs_id_record_t
        public const ulong ECS_ID_FLAGS_MASK = (0xFFul << 60);
        public const ulong ECS_ENTITY_MASK = (0xFFFFFFFFul);
        public const ulong ECS_GENERATION_MASK = (0xFFFFul << 32);
        public const ulong ECS_COMPONENT_MASK = (~ECS_ID_FLAGS_MASK);
        public const ulong ECS_PAIR = (1ul << 63);
        public const ulong ECS_OVERRIDE = (1ul << 62);
        public const ulong ECS_TOGGLE = (1ul << 61);
        public const ulong ECS_AND = (1ul << 60);

        public ecs_table_cache_t cache; /* table_cache<ecs_table_record_t> */
        public readonly ulong Id;
        public RecordFlags Flags;

        [System.Runtime.CompilerServices.InlineArray(5)]
        public unsafe struct RecordSet {
            public RecordElem Value;
            public const int First = 0, Second = 1, Trav = 2;
        }
        public RecordSet Lists;
        public IdRecord? Parent;
        public TypeInfo TypeInfo;

        public IdRecord(ulong id) {
            Id = id;
        }
        public static uint ecs_entity_t_lo(ulong value) => (uint)value;
        public static uint ecs_entity_t_hi(ulong value) => (uint)((value) >> 32);
        public static ulong ecs_entity_t_comb(uint lo, uint hi) => (((ulong)hi << 32) + (ulong)lo);
        public static ulong ecs_pair(uint pred, uint obj) => (ECS_PAIR | ecs_entity_t_comb(obj, pred));
        public static bool ECS_IS_PAIR(ulong id) => (((id) & ECS_ID_FLAGS_MASK) == ECS_PAIR);
        public static uint ECS_PAIR_FIRST(ulong e) => (ecs_entity_t_hi(e & ECS_COMPONENT_MASK));
        public static uint ECS_PAIR_SECOND(ulong e) => (ecs_entity_t_lo(e));
        public static ulong RecordHash(ulong id) {
            if ((id & ECS_ID_FLAGS_MASK) == 0) id &= ~ECS_GENERATION_MASK;
            if (ECS_IS_PAIR(id)) {
                var r = ECS_PAIR_FIRST(id);
                var o = ECS_PAIR_SECOND(id);
                if (r == (uint)EntityId.EcsAny) r = (uint)EntityId.EcsWildcard;
                if (o == (uint)EntityId.EcsAny) o = (uint)EntityId.EcsWildcard;
                id = ecs_pair(r, o);
            }
            return id;
        }
        public static bool ecs_id_is_wildcard(ulong id) {
            if ((id == (ulong)EntityId.EcsWildcard) || (id == (ulong)EntityId.EcsAny)) {
                return true;
            }

            bool is_pair = ECS_IS_PAIR(id);
            if (!is_pair) {
                return false;
            }

            var first = ECS_PAIR_FIRST(id);
            var second = ECS_PAIR_SECOND(id);

            return (first == (ulong)EntityId.EcsWildcard) || (second == (ulong)EntityId.EcsWildcard) ||
                   (first == (ulong)EntityId.EcsAny) || (second == (ulong)EntityId.EcsAny);
        }

        static void flecs_id_record_elem_insert(IdRecord head, IdRecord idr, int elemId) {
            ref var elem = ref idr.Lists[elemId];
            ref var headElem = ref head.Lists[elemId];
            IdRecord existing = headElem.Next;
            elem.Next = existing;
            elem.Previous = head;
            if (existing != null) {
                ref var existingElem = ref existing.Lists[elemId];
                existingElem.Previous = idr;
            }
            headElem.Next = idr;
        }
        public void Insert(ulong wildcard, IdRecord widr) {
            Debug.Assert(ecs_id_is_wildcard(wildcard));
            Debug.Assert(widr != null, "Changed this - originally created a new record matching the wildcard");
            if (ECS_PAIR_SECOND(wildcard) == (uint)EntityId.EcsWildcard) {
                Debug.Assert(ECS_PAIR_FIRST(wildcard) != (uint)EntityId.EcsWildcard);
                flecs_id_record_elem_insert(widr, this, RecordSet.First);
            } else {
                Debug.Assert(ECS_PAIR_FIRST(wildcard) != (uint)EntityId.EcsWildcard);
                flecs_id_record_elem_insert(widr, this, RecordSet.Second);

                if ((Flags & RecordFlags.EcsIdTraversable) != 0) {
                    flecs_id_record_elem_insert(widr, this, RecordSet.Trav);
                }
            }
        }

        public ecs_table_record_t GetTable(ecs_table_t table) {
            return cache.Get(table);
        }
    }
    public class IdRecordCollection {
        public const int FLECS_HI_ID_RECORD_ID = (1024);
        private IdRecord[] recordsArray;
        private Dictionary<ulong, IdRecord>? recordsMap;
        public IdRecordCollection() {
            recordsArray = new IdRecord[FLECS_HI_ID_RECORD_ID];
        }
        public IdRecord AllocateRecord(ulong id) {  //flecs_id_record_new
            var record = new IdRecord(id);

            bool is_wildcard = IdRecord.ecs_id_is_wildcard(id);
            bool is_pair = IdRecord.ECS_IS_PAIR(id);

            ulong rel = 0, tgt = 0;
            ulong role = (id & IdRecord.ECS_ID_FLAGS_MASK);
            if (is_pair) {
                // rel = ecs_pair_first(world, id);
                rel = IdRecord.ECS_PAIR_FIRST(id);
                Debug.Assert(rel != 0);

                /* Relationship object can be 0, as tables without a ChildOf 
                 * relationship are added to the (ChildOf, 0) id record */
                tgt = IdRecord.ECS_PAIR_SECOND(id);
                if (!is_wildcard && (rel != (ulong)EntityId.EcsFlag)) {
                    /* Inherit flags from (relationship, *) record */
                    var idr_r = RecordEnsure(IdRecord.ecs_pair((uint)rel, (uint)EntityId.EcsWildcard));
                    record.Parent = idr_r;
                    record.Flags = idr_r.Flags;

                    /* If pair is not a wildcard, append it to wildcard lists. These 
                     * allow for quickly enumerating all relationships for an object, 
                     * or all objecs for a relationship. */
                    record.Insert(IdRecord.ecs_pair((uint)rel, (int)EntityId.EcsWildcard), idr_r);

                    var idr_t = RecordEnsure(IdRecord.ecs_pair((uint)EntityId.EcsWildcard, (uint)tgt));
                    record.Insert(IdRecord.ecs_pair((uint)EntityId.EcsWildcard, (uint)tgt), idr_t);

                    if (rel == (ulong)EntityId.EcsUnion) {
                        record.Flags |= RecordFlags.EcsIdUnion;
                    }
                }
            } else {
                rel = id & IdRecord.ECS_COMPONENT_MASK;
                Debug.Assert(rel != 0);
            }
            /* Initialize type info if id is not a tag */
            if (!is_wildcard && (role == 0 || is_pair)) {
                if ((record.Flags & RecordFlags.EcsIdTag) == 0) {
                    var ti = TypeInfo.Get(rel);
                    if (ti == null && tgt != 0) {
                        ti = TypeInfo.Get(tgt);
                    }
                    record.TypeInfo = ti;
                }
            }

            var hash = IdRecord.RecordHash(id);
            if (hash < FLECS_HI_ID_RECORD_ID) {
                recordsArray[hash] = record;
            } else {
                if (recordsMap == null) recordsMap = new();
                recordsMap[hash] = record;
            }
            return record;
        }
        public IdRecord RecordEnsure(ulong id) {    // flecs_id_record_ensure
            var record = GetRecord(id);
            if (record == null) record = AllocateRecord(id);
            return record;
        }
        public IdRecord? GetRecord(ulong id) {      // flecs_id_record_get
            var hash = IdRecord.RecordHash(id);
            if (hash < FLECS_HI_ID_RECORD_ID) {
                var record = recordsArray[hash];
                if (record.Id != 0) return null;
                return record;
            } else {
                return recordsMap?[hash];
            }
        }
    }

    public class Stage : IDisposable {
        public void Dispose() {
            throw new NotImplementedException();
        }
    }

    public class Storage {
        public ecs_table_t Root;
        public Dictionary<ulong, ecs_table_t> TableMap = new();
    }

    public class World : IDisposable {

        public WorldFlags Flags;
        public ulong FrameCount;

        public Action? PostFrameActions;

        public Storage Store;
        public IdRecordCollection Records;
        public List<Stage> Stages = new();
        int event_id;

        public void Dispose() {
            
        }

        public void Progress() {
            FrameBegin();
            if (FrameCount == 0) RunStartupSystems();
            ProgressWorker();
            FrameEnd();
        }

        public void FrameBegin() {
            Debug.Assert((Flags & WorldFlags.Readonly) == 0, "Cannot tick a read-only world");

        }
        public void FrameEnd() {
            Debug.Assert((Flags & WorldFlags.Readonly) == 0, "Cannot tick a read-only world");
            ++FrameCount;
            PostFrameActions?.Invoke();
            PostFrameActions = null;
        }

        private void RunStartupSystems() {
            
        }
        private void ProgressWorker() {
            
        }

        private ulong ComputeHash(Span<ulong> type) {
            ulong hash = 0;
            foreach (var item in type) hash = (hash * 1251) ^ (hash >> 4) + item;
            return hash;
        }
        public ecs_table_t TableEnsure(Span<ulong> type, ecs_table_t prev) {
            var id_count = type.Length;
            if (id_count == 0) return Store.Root;
            var hash = ComputeHash(type);
            if (Store.TableMap.TryGetValue(hash, out var table)) return table;
            Debug.Assert((Flags & WorldFlags.Readonly) == 0);
            return CreateTable(type, prev);
        }

        private ecs_table_t CreateTable(Span<ulong> type, ecs_table_t prev) {
            var hash = ComputeHash(type);
            var table = new ecs_table_t() {
                id = (uint)Store.TableMap.Count,
                type = type.ToArray(),
                _ = new() {
                    hash = hash,
                    refcount = 1,
                },
            };
            Store.TableMap.Add(hash, table);
            return table;
        }

        public ref ecs_record_t flecs_new_entity(
            uint entity,
            ref ecs_record_t record,
            ecs_table_t table,
            ref ecs_table_diff_t diff,
            bool ctor,
            EventFlags evt_flags) {
            var row = table.flecs_table_append(entity, record, ctor, true);
            record.table = table;
            record.row = ecs_record_t.ECS_ROW_TO_RECORD(row, record.row & ecs_record_t.ECS_ROW_FLAGS_MASK);

            Debug.Assert(table.data.entities.Count > row);
            flecs_notify_on_add(table, null, row, 1, diff.added, evt_flags);

            return ref record;
        }
        public void flecs_notify_on_add(
            ecs_table_t table, ecs_table_t other_table,
            int row, int count,
            ecs_type_t added, EventFlags flags) {
            Debug.Assert(added != null);

            if (added.Count != 0) {
                var table_flags = table.flags;

                if ((table_flags & TableFlags.EcsTableHasUnion) != 0) {
                    flecs_set_union(table, row, count, added);
                }

                if ((table_flags & (TableFlags.EcsTableHasOnAdd | TableFlags.EcsTableHasIsA | TableFlags.EcsTableHasTraversable)) != 0) {
                    flecs_emit(this, this, new ecs_event_desc_t {
                        eventType = EntityId.EcsOnAdd,
                        ids = added,
                        table = table,
                        other_table = other_table,
                        offset = row,
                        count = count,
                        observable = this,
                        flags = flags
                    });
                }
            }
        }

        private void flecs_emit(World world1, World world2, ecs_event_desc_t ecs_event_desc_t) {
            throw new NotImplementedException();
        }

        void flecs_set_union(ecs_table_t table,
            int row, int count, ecs_type_t ids)
        {
            for (var i = 0; i < ids.Count; i ++) {
                var id = ids[i];

                if (EntityIndex.ECS_HAS_ID_FLAG(id, IdRecord.ECS_PAIR)) {
                    var idr = Records.GetRecord(
                        IdRecord.ecs_pair((uint)EntityId.EcsUnion, IdRecord.ECS_PAIR_FIRST(id)));
                    if (idr == null) continue;

                    var tr = idr.GetTable(table);
                    Debug.Assert(tr != null);
                    Debug.Assert(table._ != null);
                    var column = tr.column - table._.sw_offset;
                    var sw = table._.sw_columns[column];
                    var union_case = EntityIndex.ECS_PAIR_SECOND(id);

                    for (var r = 0; r < count; r ++) {
                        sw.flecs_switch_set(row + r, union_case);
                    }
                }
            }
        }

        void flecs_emit(World stage, ecs_event_desc_t desc) {
            Debug.Assert(desc.eventType != 0);
            Debug.Assert(desc.eventType != EntityId.EcsWildcard);
            Debug.Assert(desc.ids != null);
            Debug.Assert(desc.ids.Count != 0);
            Debug.Assert(desc.table != null);
            Debug.Assert(desc.observable != null);

            var ids = desc.ids;
            var eventType = desc.eventType;
            ecs_table_t table = desc.table, other_table = desc.other_table;
            var offset = desc.offset;
            var count = desc.count;
            var table_flags = table.flags;

            /* Table events are emitted for internal table operations only, and do not
             * provide component data and/or entity ids. */
            bool table_event = (desc.flags & EventFlags.EcsEventTableOnly) != 0;
            if (count == 0 && !table_event) {
                /* If no count is provided, forward event for all entities in table */
                count = table.ecs_table_count() - offset;
            }

            /* When the NoOnSet flag is provided, no OnSet/UnSet events should be 
             * generated when new components are inherited. */
            bool no_on_set = (desc.flags & EventFlags.EcsEventNoOnSet) != 0;

            ulong ids_cache = 0;
            object ptrs_cache = null;
            int sizes_cache = 0;
            int columns_cache = 0;
            EntityId sources_cache = 0;

            ecs_iter_t it = new ecs_iter_t {
                world = stage,
                real_world = this,
                eventType = eventType,
                table = table,
                field_count = 1,
                ids = &ids_cache,
                ptrs = &ptrs_cache,
                sizes = &sizes_cache,
                columns = &columns_cache,
                sources = &sources_cache,
                other_table = other_table,
                offset = offset,
                count = count,
                param = desc.param,
                flags = desc.flags | EcsIterIsValid
            };

            /* The world event id is used to determine if an observer has already been
             * triggered for an event. Observers for multiple components are split up
             * into multiple observers for a single component, and this counter is used
             * to make sure a multi observer only triggers once, even if multiple of its
             * single-component observers trigger. */
            var evtx = ++event_id;

            var observable = (desc.observable);
            Debug.Assert(observable != null);

            /* Event records contain all observers for a specific event. In addition to
             * the emitted event, also request data for the Wildcard event (for 
             * observers subscribing to the wildcard event), OnSet and UnSet events. The
             * latter to are used for automatically emitting OnSet/UnSet events for 
             * inherited components, for example when an IsA relationship is added to an
             * entity. This doesn't add much overhead, as fetching records is cheap for
             * builtin event types. */
            var er = flecs_event_record_get_if(observable, eventType);
            var wcer = flecs_event_record_get_if(observable, EntityId.EcsWildcard);
            var er_onset = flecs_event_record_get_if(observable, EntityId.EcsOnSet);
            var er_unset = flecs_event_record_get_if(observable, EntityId.EcsUnSet);

            ecs_data_t storage = default;
            List<byte> columns = default;
            if (count != 0) {
                storage = table.data;
                columns = storage.columns;
                it.entities = CollectionsMarshal.AsSpan(storage.entities).Slice(offset);
            }

            var id_count = ids.Count;
            var id_array = ids;

            /* If a table has IsA relationships, OnAdd/OnRemove events can trigger 
             * (un)overriding a component. When a component is overridden its value is
             * initialized with the value of the overridden component. */
            bool can_override = count != 0 && (table_flags & TableFlags.EcsTableHasIsA) != 0 && (
                (eventType == EntityId.EcsOnAdd) || (eventType == EntityId.EcsOnRemove));

            /* When a new (traversable) relationship is added (emitting an OnAdd/OnRemove
             * event) this will cause the components of the target entity to be 
             * propagated to the source entity. This makes it possible for observers to
             * get notified of any new reachable components though the relationship. */
            bool can_forward = eventType != EntityId.EcsOnSet;

            /* Set if event has been propagated */
            bool propagated = false;

            /* Does table has observed entities */
            bool has_observed = (table_flags & TableFlags.EcsTableHasTraversable) != 0;

            /* When a relationship is removed, the events reachable through that 
             * relationship should emit UnSet events. This is part of the behavior that
             * allows observers to be agnostic of whether a component is inherited. */
            bool can_unset = count != 0 && eventType == EntityId.EcsOnRemove && !no_on_set;

            Span<ecs_event_id_record_t> iders = stackalloc ecs_event_id_record_t[5]{ 0};
            int unset_count = 0;

            /* This is the core event logic, which is executed for each event. By 
             * default this is just the event kind from the ecs_event_desc_t struct, but
             * can also include the Wildcard and UnSet events. The latter is emitted as
             * counterpart to OnSet, for any removed ids associated with data. */
            for (var i = 0; i < id_count; i ++) {
                /* Emit event for each id passed to the function. In most cases this 
                 * will just be one id, like a component that was added, removed or set.
                 * In some cases events are emitted for multiple ids.
                 * 
                 * One example is when an id was added with a "With" property, or 
                 * inheriting from a prefab with overrides. In these cases an entity is 
                 * moved directly to the archetype with the additional components. */
                IdRecord idr = null;
                TypeInfo ti = null;
                ulong id = id_array[i];
                int ider_i, ider_count = 0;
                bool is_pair = EntityIndex.ECS_IS_PAIR(id);
                object override_ptr = null;
                ulong baseEntity = 0;

                /* Check if this id is a pair of an traversable relationship. If so, we 
                 * may have to forward ids from the pair's target. */
                if ((can_forward && is_pair) || can_override) {
                    idr = flecs_query_id_record_get(world, id);
                    var idr_flags = idr.flags;

                    if (is_pair && (idr_flags & RecordFlags.EcsIdTraversable)) {
                        ecs_event_record_t *er_fwd = null;
                        if (EntityIndex.ECS_PAIR_FIRST(id) == (ulong)EntityId.EcsIsA) {
                            if (eventType == EntityId.EcsOnAdd) {
                                if (!world->stages[0].base) {
                                    /* Adding an IsA relationship can trigger prefab
                                     * instantiation, which can instantiate prefab 
                                     * hierarchies for the entity to which the 
                                     * relationship was added. */
                                    var tgt = EntityIndex.ECS_PAIR_SECOND(id);

                                    /* Setting this value prevents flecs_instantiate 
                                     * from being called recursively, in case prefab
                                     * children also have IsA relationships. */
                                    world->stages[0].base = tgt;
                                    flecs_instantiate(world, tgt, table, offset, count);
                                    world->stages[0].base = 0;
                                }

                                /* Adding an IsA relationship will emit OnSet events for
                                 * any new reachable components. */
                                er_fwd = er_onset;
                            } else if (eventType == EntityId.EcsOnRemove) {
                                /* Vice versa for removing an IsA relationship. */
                                er_fwd = er_unset;
                            }
                        }

                        /* Forward events for components from pair target */
                        flecs_emit_forward(world, er, er_fwd, ids, &it, table, idr, evtx);
                    }

                    if (can_override && (!(idr_flags & EcsIdDontInherit))) {
                        /* Initialize overridden components with value from base */
                        ti = idr.type_info;
                        if (ti) {
                            ecs_table_record_t *base_tr = NULL;
                            int32_t base_column = ecs_search_relation(world, table, 
                                0, id, EcsIsA, EcsUp, &base, NULL, &base_tr);
                            if (base_column != -1) {
                                /* Base found with component */
                                ecs_table_t *base_table = base_tr->hdr.table;
                                base_column = ecs_table_type_to_storage_index(
                                    base_table, base_tr->column);
                                ecs_assert(base_column != -1, ECS_INTERNAL_ERROR, NULL);
                                ecs_record_t *base_r = flecs_entities_get(world, base);
                                ecs_assert(base_r != NULL, ECS_INTERNAL_ERROR, NULL);
                                int32_t base_row = ECS_RECORD_TO_ROW(base_r->row);
                                ecs_vec_t *base_v = &base_table->data.columns[base_column];
                                override_ptr = ecs_vec_get(base_v, ti->size, base_row);
                            }
                        }
                    }
                }

                if (er) {
                    /* Get observer sets for id. There can be multiple sets of matching
                     * observers, in case an observer matches for wildcard ids. For
                     * example, both observers for (ChildOf, p) and (ChildOf, *) would
                     * match an event for (ChildOf, p). */
                    ider_count = flecs_event_observers_get(er, id, iders);
                    idr = idr ? idr : flecs_query_id_record_get(world, id);
                    ecs_assert(idr != NULL, ECS_INTERNAL_ERROR, NULL);
                }

                if (can_unset) {
                    /* Increase UnSet count in case this is a component (has data). This
                     * will cause the event loop to be ran again as UnSet event. */
                    idr = idr ? idr : flecs_query_id_record_get(world, id);
                    ecs_assert(idr != NULL, ECS_INTERNAL_ERROR, NULL);
                    unset_count += (idr->type_info != NULL);
                }

                if (!ider_count && !override_ptr) {
                    /* If nothing more to do for this id, early out */
                    continue;
                }

                ecs_assert(idr != NULL, ECS_INTERNAL_ERROR, NULL);
                const ecs_table_record_t *tr = flecs_id_record_get_table(idr, table);
                if (tr == NULL) {
                    /* When a single batch contains multiple add's for an exclusive
                     * relationship, it's possible that an id was in the added list
                     * that is no longer available for the entity. */
                    continue;
                }

                var column = tr.column;
                var storage_i = -1;
                it.columns[0] = column + 1;
                it.ptrs[0] = NULL;
                it.sizes[0] = 0;
                it.event_id = id;
                it.ids[0] = id;

                if (count != 0) {
                    storage_i = ecs_table_type_to_storage_index(table, column);
                    if (storage_i != -1) {
                        /* If this is a component, fetch pointer & size */
                        ecs_assert(idr->type_info != NULL, ECS_INTERNAL_ERROR, NULL);
                        ecs_vec_t *vec = &columns[storage_i];
                        ecs_size_t size = idr->type_info->size;
                        void *ptr = ecs_vec_get(vec, size, offset);
                        it.sizes[0] = size;

                        if (override_ptr) {
                            if (eventType == EntityId.EcsOnAdd) {
                                /* If this is a new override, initialize the component
                                 * with the value of the overridden component. */
                                flecs_override_copy(
                                    world, table, ti, ptr, override_ptr, offset, count);
                            } else if (er_onset) {
                                /* If an override was removed, this re-exposes the
                                 * overridden component. Because this causes the actual
                                 * (now inherited) value of the component to change, an
                                 * OnSet event must be emitted for the base component.*/
                                ecs_assert(eventType == EntityId.EcsOnRemove, ECS_INTERNAL_ERROR, NULL);
                                ecs_event_id_record_t *iders_set[5] = {0};
                                int32_t ider_set_i, ider_set_count = 
                                    flecs_event_observers_get(er_onset, id, iders_set);
                                if (ider_set_count) {
                                    /* Set the source temporarily to the base and base
                                     * component pointer. */
                                    it.sources[0] = base;
                                    it.ptrs[0] = ptr;
                                    for (ider_set_i = 0; ider_set_i < ider_set_count; ider_set_i ++) {
                                        ecs_event_id_record_t *ider = iders_set[ider_set_i];
                                        flecs_observers_invoke(world, &ider->self_up, &it, table, EcsIsA, evtx);
                                        flecs_observers_invoke(world, &ider->up, &it, table, EcsIsA, evtx);
                                    }
                                    it.sources[0] = 0;
                                }
                            }
                        }

                        it.ptrs[0] = ptr;
                    } else {
                        if (it.eventType == EntityId.EcsUnSet) {
                            /* Only valid for components, not tags */
                            continue;
                        }
                    }
                }

                /* Actually invoke observers for this event/id */
                for (ider_i = 0; ider_i < ider_count; ider_i ++) {
                    ecs_event_id_record_t *ider = iders[ider_i];
                    flecs_observers_invoke(world, &ider->self, &it, table, 0, evtx);
                    flecs_observers_invoke(world, &ider->self_up, &it, table, 0, evtx);
                }

                if (!ider_count || !count || !has_observed) {
                    continue;
                }

                /* If event is propagated, we don't have to manually invalidate entities
                 * lower in the tree(s). */
                propagated = true;

                /* The table->traversable_count value indicates if the table contains any
                 * entities that are used as targets of traversable relationships. If the
                 * entity/entities for which the event was generated is used as such a
                 * target, events must be propagated downwards. */
                ecs_entity_t *entities = it.entities;
                it.entities = NULL;

                ecs_record_t **recs = ecs_vec_get_t(&storage->records, 
                    ecs_record_t*, offset);
                for (var r = 0; r < count; r ++) {
                    ecs_record_t *record = recs[r];
                    if (!record) {
                        /* If the event is emitted after a bulk operation, it's possible
                         * that it hasn't been populated with entities yet. */
                        continue;
                    }

                    ecs_id_record_t *idr_t = record->idr;
                    if (idr_t) {
                        /* Entity is used as target in traversable pairs, propagate */
                        ecs_entity_t e = entities[r];
                        it.sources[0] = e;
                        flecs_emit_propagate(world, &it, idr, idr_t, iders, ider_count);
                    }
                }

                it.table = table;
                it.other_table = other_table;
                it.entities = entities;
                it.count = count;
                it.offset = offset;
                it.sources[0] = 0;
            }

            if (count && can_forward && has_observed && !propagated) {
                flecs_emit_propagate_invalidate(world, table, offset, count);
            }

            can_override = false; /* Don't override twice */
            can_unset = false; /* Don't unset twice */
            can_forward = false; /* Don't forward twice */

            if (unset_count && er_unset && (er != er_unset)) {
                /* Repeat event loop for UnSet event */
                unset_count = 0;
                er = er_unset;
                it.event = EcsUnSet;
                goto repeat_event;
            }

            if (wcer && er != wcer) {
                /* Repeat event loop for Wildcard event */
                er = wcer;
                it.event = event;
                goto repeat_event;
            }
        }

    }
}
