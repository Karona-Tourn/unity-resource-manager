using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace TK.ResourceManagement
{
	/// <summary>
	/// Manage loading and caching resources
	/// </summary>
	public class ResourceManager : MonoBehaviour
	{
		private class ResourceLoader : IEnumerator
		{
			private LoadTask _task;
			private ResourceRequest _request = null;
			private UnityEngine.Object[] _assets = null;

			public ResourceLoader ( LoadTask task )
			{
				_task = task;
			}

			public object Current { get { return null; } }

			public bool MoveNext ()
			{
				if ( _request == null || _request.isDone )
				{
					if ( _request != null )
					{
						if ( _request.asset != null ) _assets = new UnityEngine.Object[] { _request.asset };
						else _assets = new UnityEngine.Object[0];
					}

					CacheIfNeeded ();

					if ( _task.completed != null )
					{
						_task.completed (_assets);
					}

					return false;
				}

				return true;
			}

			private void CacheIfNeeded ()
			{
				if ( !_task.shouldCache || _assets == null || _assets.Length == 0 ) return;

				List<UnityEngine.Object> assetList = null;

				string key = _task.SafePath;

				if ( Instance._assets.TryGetValue ( key, out assetList ) )
				{
					for ( int i = 0; i < _assets.Length; i++ )
					{
						assetList.Add ( _assets[i] );
					}
				}
				else
				{
					assetList = new List<UnityEngine.Object> ();
					for ( int i = 0; i < _assets.Length; i++ )
					{
						assetList.Add ( _assets[i] );
					}
					Instance._assets.Add ( key, assetList );
				}
			}

			public void Reset ()
			{
				if ( _task.HasPath )
				{
					string path = _task.SafePath;

					if ( _task.useAsync )
					{
						if ( _task.type == null ) _request = Resources.LoadAsync ( path );
						else _request = Resources.LoadAsync ( path, _task.type );
					}
					else
					{
						if ( _task.type == null ) _assets = Resources.LoadAll ( path );
						else _assets = Resources.LoadAll ( path, _task.type );
					}
				}
				else
				{
					if ( _task.type == null ) _assets = Resources.LoadAll ( "" );
					else _assets = Resources.LoadAll ( "", _task.type );
				}
			}

			public void Unload ()
			{
				if ( _request != null && _request.isDone && _request.asset != null ) Resources.UnloadAsset ( _request.asset );

				if ( _assets != null && _assets.Length > 0 )
				{
					for ( int i = _assets.Length - 1; i >= 0; i-- )
					{
						Resources.UnloadAsset ( _assets[i] );
					}
				}
			}
		}

		public struct LoadTask
		{
			public string path;
			public Type type;
			public bool useAsync;
			public bool shouldCache;
			public UnityAction<UnityEngine.Object[]> completed;

			public bool HasPath { get { return !string.IsNullOrEmpty ( path ); } }

			public string SafePath { get { return HasPath ? path.Trim () : ""; } }
		}

		static private ResourceManager instance = null;

		private Dictionary<string, List<UnityEngine.Object>> _assets = new Dictionary<string, List<UnityEngine.Object>>();
		private Queue<LoadTask> _taskQueue = new Queue<LoadTask>();
		private ResourceLoader _runningLoader = null;

		static private ResourceManager Instance
		{
			get
			{
				if ( instance == null )
				{
					new GameObject ( "_ResourceManager", typeof ( ResourceManager ) );
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

						if ( !_assets.ContainsKey ( task.SafePath ) )
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
				if ( !_runningLoader.MoveNext () ) _runningLoader = null;
			}
		}

		private void LoadInternal ( LoadTask task )
		{
			string key = task.SafePath;
			List<UnityEngine.Object> assetList = null;

			if ( _assets.TryGetValue ( key, out assetList ) )
			{
				if ( task.completed != null ) task.completed ( assetList.ToArray () );
			}
			else
			{
				_taskQueue.Enqueue ( task );
			}
		}

		private void UnloadInternal (string path)
		{
			List<UnityEngine.Object> assetList = null;

			if ( _assets.TryGetValue ( path, out assetList ) )
			{
				for ( int i = assetList.Count - 1; i >= 0; i-- )
				{
					if ( assetList[i] is GameObject ) continue;

					Resources.UnloadAsset ( assetList[i] );
				}
				assetList.Clear ();
				_assets.Remove ( path );
			}
		}

		private void UnloadAllInternal ()
		{
			foreach ( var kv in _assets )
			{
				for ( int i = kv.Value.Count - 1; i >= 0; i-- )
				{
					if ( kv.Value[i] is GameObject ) continue;

					Resources.UnloadAsset ( kv.Value[i] );
				}

				kv.Value.Clear ();
			}

			_assets.Clear ();
		}

		private void StopInternal ()
		{
			_taskQueue.Clear ();

			if ( _runningLoader != null )
			{
				_runningLoader.Unload ();
				_runningLoader = null;
			}
		}

		static public void Stop ()
		{
			Instance.StopInternal ();
		}

		static public void Load ( params LoadTask[] tasks )
		{
			for ( int i = 0; i < tasks.Length; i++ )
			{
				Instance.LoadInternal ( tasks[i] );
			}
		}

		static public void Unload ( params string[] paths )
		{
			for ( int i = 0; i < paths.Length; i++ )
			{
				Instance.UnloadInternal ( paths[i] );
			}
		}

		static public void UnloadAll ()
		{
			Instance.UnloadAllInternal ();
		}
	}
}
