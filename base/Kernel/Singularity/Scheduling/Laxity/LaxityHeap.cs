////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   LaxityHeap.cs
//
//  Note:
//

using System;
using System.Collections;
using System.Diagnostics;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling.Laxity
{
    /// <summary>
    /// A generic heap implementation
    /// </summary>
    public class Heap
    {
        private class HeapElem
        {
            public object elem;
            public IComparable key;

            public HeapElem(object elem, IComparable key)
            {
                this.elem = elem;
                this.key = key;
            }
        }

        private Hashtable map;
        private ArrayList heap;

        // float the value at position i down the heap, assuming
        // subtrees already satisfy the heap property
        // this is HEAPIFY from CLR pg 143.
        private void pushdown(int i)
        {
            while (i*2 + 1 < heap.Count) {
                int ichild = i*2 + 1;
                HeapElem child = (HeapElem)heap[ichild];
                if (ichild + 1 < heap.Count) {
                    HeapElem otherchild = (HeapElem)heap[ichild+1];
                    if (otherchild.key.CompareTo(child.key) < 0) {
                        child = otherchild;
                        ichild++;
                    }
                }

                HeapElem cur = (HeapElem)heap[i];
                // now child contains the child with the smaller key
                // if that is smaller than parent's key, swap them
                if (child.key.CompareTo(cur.key) < 0) {
                    heap[i] = child;
                    heap[ichild] = cur;
                    map[child.elem] = i;
                    map[cur.elem] = ichild;
                }

                // and continue one level down
                i = ichild;
            }
        }

        // push a value up the heap until it exceeds its parent
        // this is  HEAP-INSERT from CLR pg 150
        private void pushup(int i)
        {
            while (i > 0) {
                HeapElem cur = (HeapElem) heap[i];
                HeapElem parent = (HeapElem) heap[(i-1)/2];
                if (parent.key.CompareTo(cur.key) <= 0) {
                    break;
                }
                // swap with parent and continue
                heap[i] = parent;
                heap[(i-1)/2] = cur;
                map[parent.elem] = i;
                map[cur.elem] = (i-1)/2;
                i = (i-1)/2;
            }
        }


        // assert heap invariant at node i
        private void assertHeap(int i)
        {
            // first make sure map and heap are consistent
            Debug.Assert(heap.Count == map.Count);
            for (int j = 0; j < heap.Count; j++) {
                HeapElem cur = (HeapElem) heap[j];
                int jj = (int) map[cur.elem];
                Debug.Assert(j == jj);
            }

            for (; i < heap.Count/2; i++) {
                HeapElem cur = (HeapElem) heap[i];
                HeapElem child = (HeapElem) heap[i*2+1];
                Debug.Assert(child.key.CompareTo(cur.key) >= 0);
                if (i*2 + 2 < heap.Count) {
                    child = (HeapElem)heap[i*2+2];
                    Debug.Assert(child.key.CompareTo(cur.key) >= 0);
                }
            }
        }

        /// <summary>
        /// Create a new, empty heap.
        /// </summary>
        public Heap()
        {
            map = new Hashtable();
            heap = new ArrayList();
        }

        public int Count
        {
            get { return heap.Count; }
        }

        /// <summary>
        /// Check if the heap is legit else assert
        /// </summary>
        public void AssertHeap()
        {
            assertHeap(0);
        }

        /// <summary>
        /// Insert an object into the heap
        /// </summary>
        /// <param name="elem">the object to be inserted</param>
        /// <param name="key">the key value with which the object is associated</param>
        /// <returns>false if object already exists in the heap (key is not updated in this case)</returns>

        public bool Insert(object elem, IComparable key)
        {
            if (map.ContainsKey(elem)) {
                return false;
            }
            int i = heap.Count;
            map[elem] = i;
            heap.Add(new HeapElem(elem, key));
            pushup(i);
            return true;
        }

        /// <summary>
        /// Update a key value for an existing object
        /// </summary>
        /// <param name="elem">the object to update the key for</param>
        /// <param name="newkey">the new key value</param>
        /// <returns>false if the object does not exist in the heap</returns>
        public bool Update(object elem, IComparable newkey)
        {
            if (!map.ContainsKey(elem)) {
                return false;
            }
            int i = (int)map[elem];
            HeapElem cur = (HeapElem) heap[i];
            cur.key = newkey;

            // we need to push it either up or down, just do both
            pushdown(i);
            pushup(i);

            return true;
        }

        /// <summary>
        /// Delete an object from the heap
        /// </summary>
        /// <param name="elem">the object to delete</param>
        /// <returns>false if object does not exist in the heap</returns>
        public bool Delete(object elem)
        {
            if (!map.ContainsKey(elem)) {
                return false;
            }

            int i = (int) map[elem];
            int ilast = heap.Count - 1;
            HeapElem lastelem = (HeapElem)heap[ilast];

            // remove from mapping and shrink the array
            heap.RemoveAt(ilast);
            map.Remove(elem);

            if (i != ilast) {
                // move erstwhile last elem into current position and update mapping
                heap[i] = lastelem;
                map[lastelem.elem] = i;

                // push up/down to rightful place
                pushdown(i);
                pushup(i);
            }

            return true;
        }

        /// <summary>
        /// Return the heap object with the least key, without removing it.
        /// </summary>
        /// <returns>the heap object with the least key</returns>
        public object Min
        {
            get { return (heap.Count > 0) ? ((HeapElem)heap[0]).elem : null; }
        }

        // debug the heap
        static void TestHeap(int maxelem)
        {
            Heap newheap = new Heap();
            Random r = new Random();
            bool[] inserted = new bool[maxelem];
            int[] updated = new int[maxelem];

            for (int i = 0; i < maxelem; i++) {
                DebugStub.WriteLine("Inserting {0}\r", i);
                int ins = r.Next() % maxelem;
                bool insret = newheap.Insert(ins, ins);
                newheap.AssertHeap();
                Debug.Assert(insret != inserted[ins]); // returns false if already inserted
                inserted[ins] = true;
            }
            DebugStub.WriteLine("{0} inserted", newheap.Count);

            for (int i = 0; i < maxelem; i++) {
                DebugStub.WriteLine("Updating {0}", i);
                int newkey = r.Next() % 1000;
                bool upret = newheap.Update(i, newkey);
                newheap.AssertHeap();
                Debug.Assert(upret == inserted[i]);
                updated[i] = upret ? newkey : -1;
            }
            DebugStub.WriteLine();

            for (int i = 0; i < maxelem; i++) {
                DebugStub.WriteLine("Deleting {0}", i);
                int del = r.Next() % maxelem;
                bool delret = newheap.Delete(del);
                newheap.AssertHeap();
                Debug.Assert(delret == inserted[del]); // returns false if not inserted
                inserted[del] = false;
            }
            DebugStub.WriteLine();

            int prev = 0;
            int c = newheap.Count;
            DebugStub.WriteLine("{0} remaining", c);
            for (int i = 0; i < c; i++) {
                int nextval = (int) newheap.Min;
                newheap.AssertHeap();
                int nextkey = updated[nextval];
                Debug.Assert(nextkey != -1);
                Debug.Assert(nextkey >= prev);
                DebugStub.Write("{0} ", nextkey);
                prev = nextkey;
                Debug.Assert(newheap.Delete(nextval));
                newheap.AssertHeap();
            }
            DebugStub.WriteLine()
            Debug.Assert(newheap.Count == 0);
            DebugStub.WriteLine("All serene ...");
        }
    }
}
