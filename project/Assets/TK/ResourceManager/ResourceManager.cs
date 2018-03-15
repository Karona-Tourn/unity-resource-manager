using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UObject = UnityEngine.Object;

namespace TK.ResourceManagement
{
	/// <summary>
	/// Manage loading and caching resources
	/// </summary>
	public class ResourceManager : MonoBehaviour
	{
		/// <summary>
		/// Handle loading asset from resource
		/// </summary>
		private class ResourceLoader : IEnumerator
		{
			private LoadTask			_task;					// Setting for loading task
			private ResourceRequest		_request	= null;		// Resource loading request
			private UObject[]			_assets		= null;     // Loaded assets from resource

			#region Private Methods

			private void CacheIfNeeded ()
			{
				if ( !_task.shouldCache || _assets == null || _assets.Length == 0 ) return;

				List<UObject> assetList = null;

				string key = _task.SafePath;

				if ( Instance._cachedAssets.TryGetValue ( key, out assetList ) )
				{
					for ( int i = 0, length = _assets.Length; i < length; i++ )
					{
						assetList.Add ( _assets[i] );
					}
				}
				else
				{
					assetList = new List<UObject> ();
					for ( int i = 0, length = _assets.Length; i < length; i++ )
					{
						assetList.Add ( _assets[i] );
					}
					Instance._cachedAssets.Add ( key, assetList );
				}
			}

			#endregion

			#region Public Methods

			public ResourceLoader ( LoadTask task )
			{
				_task = task;
			}

			~ResourceLoader ()
			{
				_request = null;
				_assets = null;
			}

			public object Current { get { return null; } }

			public bool MoveNext ()
			{
				if ( _request == null || _request.isDone )
				{
					if ( _request != null )
					{
						if ( _request.asset != null ) _assets = new UObject[] { _request.asset };
						else _assets = new UObject[0];
					}

					CacheIfNeeded ();

					_task.NotifyCompleted ( _assets );

					return false;
				}

				return true;
			}

			public void Reset ()
			{
				if ( _task.HasPath )
				{
					string path = _task.SafePath;

					if ( _task.useAsync )
					{
						if ( _task.HasType ) _request = Resources.LoadAsync ( path, _task.type );
						else _request = Resources.LoadAsync ( path );
					}
					else
					{
						if ( _task.HasType ) _assets = Resources.LoadAll ( path, _task.type );
						else _assets = Resources.LoadAll ( path );
					}
				}
				else
				{
					if ( _task.HasType ) _assets = Resources.LoadAll ( "", _task.type );
					else _assets = Resources.LoadAll ( "" );
				}
			}

			public void Unload ()
			{
				if ( _request != null && _request.isDone && _request.asset != null ) Resources.UnloadAsset ( _request.asset );

				if ( _assets != null )
				{
					for ( int i = _assets.Length - 1; i >= 0; i-- )
					{
						Resources.UnloadAsset ( _assets[i] );
					}
				}
			}

			#endregion
		}

		public struct LoadTask
		{
			public string					path;			// Path of resource to be loaded
			public Type						type;			// Used for loading a resouce by type
			public bool						useAsync;		// Should load a resouces async
			public bool						shouldCache;	// Should cache the loaded resource
			public UnityAction<UObject[]>	completed;		// Callback invoked after a resource is loaded

			public bool HasType { get { return type != null; } }

			public bool HasPath { get { return !string.IsNullOrEmpty ( path ); } }

			public string SafePath { get { return HasPath ? path.Trim () : ""; } }

			public void NotifyCompleted ( UObject[] objects )
			{
				if ( completed == null ) return;
				completed ( objects );
			}
		}

		static private ResourceManager				instance		= null;

		// Dictionary to store cahced loaded resouces
		private Dictionary<string, List<UObject>>	_cachedAssets	= new Dictionary<string, List<UObject>>();

		// Queue of tasks to be executed to load resources
		private Queue<LoadTask>						_taskQueue		= new Queue<LoadTask>();

		// Executing resource loader
		private ResourceLoader						_runningLoader	= null;

		private bool                                _isRunning      = false;

		#region Private Methods

		static private ResourceManager Instance
		{
			get
			{
				if ( instance == null )
				{
					instance = FindObjectOfType<ResourceManager> ();
					if ( instance != null ) DontDestroyOnLoad ( instance.gameObject );
					else new GameObject ( "_ResourceManager", typeof ( ResourceManager ) );
				}

				return instance;
			}
		}

		private void Awake ()
		{
			if ( instance == null )
			{
				instance = this;
				DontDestroyOnLoad ( gameObject );
			}
		}

		private void Start ()
		{
			if ( !Equals ( instance ) )
			{
				Destroy ( gameObject );
			}
		}

		private void Update ()
		{
			if ( _taskQueue.Count > 0 )
			{
				if ( _runningLoader == null )
				{
					do
					{
						var task = _taskQueue.Dequeue();

						if ( !_cachedAssets.ContainsKey ( task.SafePath ) )
						{
							_runningLoader = new ResourceLoader ( task );
							_runningLoader.Reset ();
							break;
						}

					} while ( _taskQueue.Count > 0 );
				}
			}

			if ( _runningLoader != null )
			{
				_isRunning = _runningLoader.MoveNext ();

				if ( !_isRunning ) _runningLoader = null;
			}
		}

		/// <summary>
		/// Load a resource
		/// </summary>
		/// <param name="task">Setting for loading a resouce</param>
		private void LoadInternal ( LoadTask task )
		{
			string key = task.SafePath;
			List<UObject> assetList = null;

			if ( _cachedAssets.TryGetValue ( key, out assetList ) )
			{
				if ( task.HasType ) assetList = assetList.FindAll ( asset => asset.GetType () == task.type );
				task.NotifyCompleted ( assetList.ToArray () );
			}
			else
			{
				_taskQueue.Enqueue ( task );
			}
		}

		private void UnloadInternal ( List<UObject> assetList )
		{
			for ( int i = assetList.Count - 1; i >= 0; i-- )
			{
				var asset = assetList[i];

				if ( asset is GameObject || asset is Component || asset is AssetBundle ) continue;

				Resources.UnloadAsset ( asset );
			}
			assetList.Clear ();
		}

		/// <summary>
		/// Unload a loaded resource
		/// </summary>
		/// <param name="path">Path of loaded resource that will be used as key to unload the resource</param>
		private void UnloadInternal ( string path )
		{
			List<UObject> assetList = null;

			if ( _cachedAssets.TryGetValue ( path, out assetList ) )
			{
				UnloadInternal ( assetList );
				_cachedAssets.Remove ( path );
			}
		}

		/// <summary>
		/// Unload all unused resources
		/// </summary>
		private void UnloadAllInternal ()
		{
			foreach ( var asset in _cachedAssets )
			{
				UnloadInternal ( asset.Value );
			}

			_cachedAssets.Clear ();
		}

		/// <summary>
		/// Used to stop tasks loading resources
		/// </summary>
		private void StopInternal ()
		{
			_taskQueue.Clear ();

			if ( _runningLoader != null )
			{
				_runningLoader.Unload ();
				_runningLoader = null;
			}
		}

		#endregion

		#region Public Methods

		/// <summary>
		/// Used to stop tasks loading resources
		/// </summary>
		static public void Stop ()
		{
			Instance.StopInternal ();
		}

		static public void Load ( params LoadTask[] tasks )
		{
			for ( int i = 0, length = tasks.Length; i < length; i++ )
			{
				Instance.LoadInternal ( tasks[i] );
			}
		}

		static public void Unload ( params string[] paths )
		{
			for ( int i = 0, length = paths.Length; i < length; i++ )
			{
				Instance.UnloadInternal ( paths[i] );
			}
		}

		static public void UnloadAll ()
		{
			Instance.UnloadAllInternal ();
		}

		#endregion
	}
}
