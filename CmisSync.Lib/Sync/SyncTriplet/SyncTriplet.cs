﻿using System;
using System.IO;
using log4net;
using CmisSync.Lib.Sync.SyncTriplet.TripletItem;

namespace CmisSync.Lib.Sync.SyncTriplet
{

    /// <summary>
    /// Direction of the sync triplet. 
    /// </summary>
    public enum DIRECTION {BIDIRECTION = 0, LOCAL2REMOTE=1, REMOTE2LOCAL=2 }

    /// <summary>
    /// Sync triplet. A sync triplet is a (LS-DB-RS) triplet which presents a file/folder object's
    /// synchronizing status.
    /// <para>  LS: local storage, presenting the file/folder on the local file system.</para>
    /// <para>  DB: Database, presenting the file/folder in the CmisSync's DB.</para>
    /// <para>  RS: rmote storage, presenting the file/folder on the remote server.</para>
    /// <para>  </para>
    /// Syncmachine's processing worker will decide which action should be executed by the triplet's
    /// status. eg: 
    /// <para>  If (LS==DB) and (DB==RS), the file on the local fs is identical to that on the 
    ///   remote server therefore the triplet is synchronized. </para>
    /// <para>  If (LS!=DB) and (DB==RS), the file is modified after the last synchronizing.</para>
    /// </summary>
    public class SyncTriplet
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="T:CmisSync.Lib.Sync.SyncTriplet.SyncTriplet"/> class.
        /// </summary>
        public SyncTriplet(bool isFolder) {
            IsFolder = isFolder;
            LocalStorage = null;
            RemoteStorage = null;
            DBStorage = null;
            Direction = DIRECTION.BIDIRECTION;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:CmisSync.Lib.Sync.SyncTriplet.SyncTriplet"/> class.
        /// </summary>
        public SyncTriplet (bool isFolder, DIRECTION dir)
        {
            IsFolder = isFolder;
            LocalStorage = null;
            RemoteStorage = null;
            DBStorage = null;
            Direction = dir;
        }

        /// <summary>
        /// The logger.
        /// </summary>
        private static readonly ILog Logger = LogManager.GetLogger(typeof(SyncTriplet));

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name { get; set; } = "";

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:CmisSync.Lib.Sync.SyncTriplet.SyncTriplet"/>.
        /// </summary>
        /// <returns>A <see cref="T:System.String"/> that represents the current <see cref="T:CmisSync.Lib.Sync.SyncTriplet.SyncTriplet"/>.</returns>
        public override string ToString () { return Name; }

        /// <summary>
        /// Whether the item is a folder or a file.
        /// </summary>
        public bool IsFolder { get; set; }

        /// <summary>
        /// Gets or sets the synchronize direction.
        /// </summary>
        /// <value>The direction.</value>
        public DIRECTION Direction { get; set; }
        /// <summary>
        /// The local storage of sync triplet.
        /// </summary>
        public LocalStorageItem LocalStorage { get; set; }

        /// <summary>
        /// The remote storage of sync triplet.
        /// </summary>
        public RemoteStorageItem RemoteStorage { get; set; }

        /// <summary>
        /// The DBS torage of sync triplet.
        /// </summary>
        public DBStorageItem DBStorage { get; set; }

        /// <summary>
        /// Gets a value indicating whether LocalStorageItem exist.
        /// </summary>
        /// <value><c>true</c> if local exist; otherwise, <c>false</c>.</value>
        public bool LocalExist { get {
                return !(null == LocalStorage) && (IsFolder ? Directory.Exists (Utils.PathCombine (LocalStorage.RootPath, LocalStorage.RelativePath)) : 
                                                   File.Exists (Utils.PathCombine (LocalStorage.RootPath, LocalStorage.RelativePath)));
            }}

        /// <summary>
        /// Gets a value indicating whether RemoteStorageItem exist.
        /// </summary>
        /// <value><c>true</c> if remote exist; otherwise, <c>false</c>.</value>
        public bool RemoteExist { get {
                return !(null == RemoteStorage);
            }}

        /// <summary>
        /// Gets a value indicating whether DBStorageItem exist.
        /// </summary>
        /// <value><c>true</c> if DBE xist; otherwise, <c>false</c>.</value>
        public bool DBExist { get {
                return (null != DBStorage) && (null != DBStorage.DBLocalPath) && (null != DBStorage.DBRemotePath);
            }}

        /// <summary>
        /// Gets a value indicating whether this <see cref="T:CmisSync.Lib.Sync.SyncTriplet.SyncTriplet"/> local eq db.
        /// Two cases:
        ///     LS ne , DB ne, therefore LS = DB
        ///     LS e, DB e, and LS relpath = DB relpath and LS chksum == DB chksum
        ///
        /// If the sync direction is Remote_to_Local, always returns true. This will simplify tryplet processor's logic.
        /// </summary>
        /// <value><c>true</c> if local eq db; otherwise, <c>false</c>.</value>
        public bool LocalEqDB {
            get {
                if (Direction == DIRECTION.REMOTE2LOCAL) return true;
                return ( !LocalExist && !DBExist ) || 
                    ( LocalExist && DBExist && LocalStorage.RelativePath == DBStorage.DBLocalPath  && 
                        ( IsFolder ? true : LocalStorage.CheckSum == DBStorage.Checksum ));
            }
        }

        /// <summary>
        /// Gets a value indicating whether this <see cref="T:CmisSync.Lib.Sync.SyncTriplet.SyncTriplet"/> remote eq db.
        /// Two cases:
        ///     RS ne, DB ne, therefore RS = DB
        ///     RS e, DB e, RS relpath = DB relpath, ( is not folder : RS lastmodi = DB lastmodi )
        ///
        /// If the sync direction is Local_to_Remote, always returns true. This will simplify tryplet processor's logic.
        /// </summary>
        /// <value><c>true</c> if remote eq db; otherwise, <c>false</c>.</value>
        public bool RemoteEqDB {
            get {
                if (Direction == DIRECTION.LOCAL2REMOTE) return true;
                return ( !DBExist && !RemoteExist) ||
                    (DBExist && RemoteExist && (RemoteStorage.RelativePath == DBStorage.DBRemotePath) && (
                        IsFolder ? true : (
                            // DB's last modification date is universal
                            ((DateTime)RemoteStorage.LastModified).ToUniversalTime().ToString() == 
                            DBStorage.ServerSideModificationDate.ToString())
                    )
                    );
            }
        }

        /// <summary>
        /// Gets the information of triplet. For debug
        /// </summary>
        /// <value>The information.</value>
        public string Information {
            get {
                return String.Format ("  %% LocalExist? {0}\n" +
                                     "     DBExist? {1}\n" +
                                     "     RemoteExist? {2}\n\n" +
                                     "     LocalRelative? {3}\n" +
                                     "     DB -local Relative? {4}\n" +
                                     "     DB -remote Relative? {5}\n" +
                                     "     RemoteRelative? {6}\n\n" +
                                     "     LocalChkSum? {7}\n" +
                                     "     DBChkSum? {8}\n\n" +
                                     "     RemoteLastModify? {9}\n" +
                                     "     DBLastModify? {10}\n" +
                                     "     Direction? {11}",
                                     LocalExist.ToString (),
                                     DBExist.ToString (),
                                     RemoteExist.ToString (),

                                     !LocalExist ? null : LocalStorage.RelativePath,
                                     !DBExist ? null : DBStorage.DBLocalPath,
                                     !DBExist ? null : DBStorage.DBRemotePath,
                                     !RemoteExist ? null : RemoteStorage.RelativePath,

                                     (IsFolder || !LocalExist ? null : LocalStorage.CheckSum),
                                     (IsFolder || !DBExist ? null : DBStorage.Checksum),

                                     !RemoteExist ? null : RemoteStorage.LastModified,
                                     !DBExist ? null : DBStorage.ServerSideModificationDate.ToString (),
                                     Direction
                                     );
            }
        }

   }
}