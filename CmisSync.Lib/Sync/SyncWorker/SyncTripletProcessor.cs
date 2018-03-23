﻿using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;

using CmisSync.Lib.Sync;
using CmisSync.Lib.Sync.SyncWorker.ProcessWorker;

using DotCMIS.Client;

namespace CmisSync.Lib.Sync.SyncWorker
{
    public class SyncTripletProcessor
    {
        private static int MaxParallelism = 4;

        private static readonly ILog Logger = LogManager.GetLogger (typeof (SyncTripletProcessor));

        private ISession session;

        /*
         * Problem:
         *     if set max upper bound in FullSyncTriplet
         * ,its TryAdd will not be blocked but directly return false.
         * 
         * Buzy wait with sleep?
         * 
         */
        public BlockingCollection<SyncTriplet.SyncTriplet> FullSyncTriplets = new BlockingCollection<SyncTriplet.SyncTriplet>();

        private ConcurrentQueue<SyncTriplet.SyncTriplet> delayedFolerDeletions = new ConcurrentQueue<SyncTriplet.SyncTriplet> ();

        private CmisSyncFolder.CmisSyncFolder cmisSyncFolder = null;

        public SyncTripletProcessor (CmisSyncFolder.CmisSyncFolder cmisSyncFolder, ISession session)
        {
            this.cmisSyncFolder = cmisSyncFolder;
            this.session = session;
        }

        public void Start() {
            ParallelOptions options = new ParallelOptions ();
            options.MaxDegreeOfParallelism = MaxParallelism;

            // Process non-folder-deletion operations
            Parallel.ForEach (Internal.SingleItemPartitioner.Create (FullSyncTriplets.GetConsumingEnumerable ()), options, 
                              (triplet) => {

                                  // ============== new approaches ===============
                                  // must use a crawler counter to record the remote / local size to decide when to process folder deletions
                                  // ProcessWorker.ProcessWorker.SyncAction action = ProcessWorker.ProcessWorker.Reduce (triplet, cmisSyncFolder);
                                  // ProcessWorker.ProcessWorker.Process (triplet, action, session, cmisSyncFolder);

                                  ProcessWorker.ProcessWorker.Process (triplet, session, cmisSyncFolder, delayedFolerDeletions);
                              });


            Console.WriteLine (" - Processing folder deletions");

            // Process folder-deletion operations 
            // One-by-One non-parallel approach yet
            List<SyncTriplet.SyncTriplet> folderDeletionList = delayedFolerDeletions.ToList();

            // Lexicographical order
            // Therefore if there are files remained in the repository, eg. local removed but remote modified, vise versa
            // the corresponding folder will be remained. 
            folderDeletionList.Sort (new FolderLexicoGraphicalComparer ());

            /*
            foreach(SyncTriplet.SyncTriplet triplet in  folderDeletionList) {
                // unset delayed
                triplet.Delayed = false;
                ProcessWorker.ProcessWorker.Process (triplet, session, cmisSyncFolder, null);
            }
            */
            DoParallelFolderDeletions (folderDeletionList);
        }

        private void DoParallelFolderDeletions(List<SyncTriplet.SyncTriplet> folders) {

            Console.WriteLine (" - Do parallel folder deletions: ");

            while (folders.Count > 0) {

                Task parallelDeletionTask = Task.Factory.StartNew (() => ParallelFolderDeletionTask() );

                if (!FullSyncTriplets.TryAdd (folders.First ())) {
                    Console.WriteLine ("  - failed add : " + folders.First ().Name);
                } else {
                    Console.WriteLine ("  - start parallel delete: " + folders.First ().Name);
                }

                string lastFolderName = folders.First ().Name;

                folders.RemoveAt (0);
                int i = 0; 
                while (i < folders.Count) {
                    if (lastFolderName.StartsWith(folders[i].Name)) {
                        // This folder can not be processed concurrently with 
                        // the last folder name, increase index
                        i++;
                    } else {
                        // This foler can be processed, remove from list and push 
                        // to fullsynctriplet
                        lastFolderName = folders [i].Name;

                        if (!FullSyncTriplets.TryAdd (folders [i])) {
                            Console.WriteLine ("  - failed add : " + folders [i].Name);
                        } else {
                            Console.WriteLine ("  - start parallel delete: " + folders [i].Name);
                        }

                        folders.RemoveAt (i);
                    }
                }

                FullSyncTriplets.CompleteAdding ();

                parallelDeletionTask.Wait ();

                Console.WriteLine ("  - one loop of parallel folder deletions completed. start next loop");
            }
        }

        private void ParallelFolderDeletionTask() {

            ParallelOptions options = new ParallelOptions ();
            options.MaxDegreeOfParallelism = MaxParallelism;

            // Process parallel folder deletion
            Parallel.ForEach (Internal.SingleItemPartitioner.Create (FullSyncTriplets.GetConsumingEnumerable ()), options,
                              (triplet) => ProcessWorker.ProcessWorker.Process (triplet, session, cmisSyncFolder, null)
                             );

        }


        private class FolderLexicoGraphicalComparer : IComparer<SyncTriplet.SyncTriplet> {

            public int Compare (SyncTriplet.SyncTriplet a, SyncTriplet.SyncTriplet b)
            {
                return 0 - Helper (a, b);
            }

            private int Helper(SyncTriplet.SyncTriplet a, SyncTriplet.SyncTriplet b) {
                // C# will split "/" by '/' to an array with 2 elements: "", '/', ""
                // will split "/a/b/c/" by '/' to an array with 5 elements: "", "a", "b", "c", ""
                // so "" does nothing to the algoritm
                String [] m = a.Name.Split ('/');
                String [] n = b.Name.Split ('/');
                int l = Math.Min (m.Length, n.Length);
                int i= 0;
                while (i <= l) {
                    if (string.Compare (m [i], n [i]) < 0) return -1;
                    if (string.Compare (m [i], n [i]) > 0) return 1;
                    i++;
                }
                if (m.Length > n.Length) return 1;
                if (m.Length < n.Length) return -1;
                return 0;
            } 
        }

        /*
        public void StartDelayedFolderDeletion () {
           // Process folder-deletion operations 
        }
        */

        ~SyncTripletProcessor ()
        {
            Dispose (false);
        }

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        private Object disposeLock = new object ();
        private bool disposed = false;
        protected virtual void Dispose (bool disposing)
        {
            lock (disposeLock) {
                if (!this.disposed) {
                    if (disposing) {
                        FullSyncTriplets.Dispose();
                    }
                    this.disposed = true;
                }
            }
        }
    }
}
