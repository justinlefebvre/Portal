using UnityEngine;
using System;
using System.Collections;
#if UNITY_EDITOR
	using UnityEditorInternal;
	using UnityEditor;
#endif

[DisallowMultipleComponent]
[ExecuteInEditMode]

[RequireComponent (typeof(MeshFilter))]
[RequireComponent (typeof(MeshRenderer))]
[RequireComponent (typeof(Rigidbody))]

public class PortalManager : MonoBehaviour {
	public bool EnablePortalTrigger = true;
	public GameObject[] ExcludedWallsFromTrigger;
	[Range(1, 31)] public int ExcludedWallsLayer = 1;
	public bool EnableMeshClipPlane = true;
	[HideInInspector] public GameObject ClipPlanePosObj;
	private Material GateMaterial;
	private Material ClipPlaneMaterial;
	private Material CloneClipPlaneMaterial;
	private Camera InGameCam;
	private Vector2 PreviousProjectionResolution;
	public Vector2 ProjectionResolution = new Vector2(1280, 1024);
	[Range(1, 20)] public int RecursionSteps = 1;
	public Material CustomRecursionEnd;
	public GameObject SecondGate;
	public Material SecondGateCustomSkybox;
	[HideInInspector] public GameObject[] GateCamObjs;
	private int[] InitGateCamObjsCullingMask;
	private bool[] InitGateCamObjsCullingMaskChecker;

	void OnEnable () {
		GateCamObjs = new GameObject[20];
		InitGateCamObjsCullingMask = new int[GateCamObjs.Length];
		InitGateCamObjsCullingMaskChecker = new bool[GateCamObjs.Length];

		//Generate "Portal" and "Clipping plane" materials
		if (!GateMaterial)
			GateMaterial = new Material (Shader.Find ("Gater/UV Remap"));

		string ClipPlaneShaderName = "Custom/StandardClippable";

		if (!ClipPlaneMaterial)
			ClipPlaneMaterial = new Material (Shader.Find (ClipPlaneShaderName));
		if (!CloneClipPlaneMaterial)
			CloneClipPlaneMaterial = new Material (Shader.Find (ClipPlaneShaderName));

		//Apply custom settings to the portal components
		GetComponent<MeshRenderer> ().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		GetComponent<MeshRenderer> ().receiveShadows = false;
		GetComponent<MeshRenderer> ().sharedMaterial = GateMaterial;

		GetComponent<Rigidbody> ().mass = 1;
		GetComponent<Rigidbody> ().drag = 0;
		GetComponent<Rigidbody> ().angularDrag = 0;
		GetComponent<Rigidbody> ().useGravity = false;
		GetComponent<Rigidbody> ().isKinematic = true;
		GetComponent<Rigidbody> ().interpolation = RigidbodyInterpolation.None;
		GetComponent<Rigidbody> ().collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
		GetComponent<Rigidbody> ().constraints = RigidbodyConstraints.None;

		if (GetComponent<MeshCollider> ()) {
			GetComponent<MeshCollider> ().convex = true;
			GetComponent<MeshCollider> ().sharedMaterial = null;
		}

		//Disable collision of portal with walls
		if (ExcludedWallsFromTrigger.Length > 0)
			for (int i = 0; i < ExcludedWallsFromTrigger.Length; i++)
				if (ExcludedWallsFromTrigger [i])
					Physics.IgnoreCollision (transform.GetComponent<Collider> (), ExcludedWallsFromTrigger [i].GetComponent<Collider> (), true);
		
		#if UNITY_EDITOR
			//Search trace of required objects for teleport, and fill the relative variables if these are already existing on the current Editor instance
			int GateCamObjsSteps = 0;

			for (int j = 0; j < transform.GetComponentsInChildren<Transform> ().Length; j++) {
				if (transform.GetComponentsInChildren<Transform> () [j].name == this.gameObject.name + " Camera " + GateCamObjsSteps) {
					GateCamObjs[GateCamObjsSteps] = transform.GetComponentsInChildren<Transform> () [j].gameObject;
						
					GateCamObjsSteps += 1;
				}

				if (transform.GetComponentsInChildren<Transform> () [j].name == transform.name + " ClipPlanePosObj")
					ClipPlanePosObj = transform.GetComponentsInChildren<Transform> () [j].gameObject;
			}
			
			EditorApplication.update = null;
			if (!EditorApplication.isPlaying)
				EditorApplication.update = EditorUpdate;
		#endif
	}

	void EditorUpdate() {
		GenGate ();

		GateCamRepos ();
	}

	void Update () {
		GenGate ();
	}
	void LateUpdate () {
		GateCamRepos ();
	}

	private string LastMetdFocusedWindowName;
	private bool MetdActiveSceneView;
	private bool FillMetdActiveSceneView = true;

	//Acquire Scene/Game camera
	Camera GetCurrentCam (Camera MetdCurrentCam) {
		#if UNITY_EDITOR
			bool MetdFocusingScene = SceneView.focusedWindow;
			string MetdFocusedWindowName = MetdFocusingScene ? SceneView.focusedWindow.titleContent.text : "";
			
			Camera MetdActiveCam = SceneView.GetAllSceneCameras ().Length > 0 ? (SceneView.GetAllSceneCameras () [0].pixelRect.max.x < Camera.main.pixelRect.max.x ? Camera.main : SceneView.GetAllSceneCameras () [0]) : MetdCurrentCam;
			
			if (!FillMetdActiveSceneView && LastMetdFocusedWindowName != MetdFocusedWindowName)
				if (MetdFocusingScene && (MetdFocusedWindowName == "Scene" || MetdFocusedWindowName == "Game")) {
					FillMetdActiveSceneView = true;
					
					LastMetdFocusedWindowName = MetdFocusedWindowName;
				}
			if (FillMetdActiveSceneView && SceneView.lastActiveSceneView) {
				MetdActiveSceneView = SceneView.FrameLastActiveSceneView ();
				
				FillMetdActiveSceneView = false;
			}
			
			MetdCurrentCam = MetdFocusingScene && !EditorApplication.isPlaying ?
									(MetdFocusedWindowName != "Scene" ?
										(MetdFocusedWindowName != "Game" ?
											(!MetdCurrentCam ?
												(MetdActiveSceneView ?
													MetdActiveCam
													: InGameCam)
												: MetdCurrentCam)
											: InGameCam)
										: MetdActiveCam)
									: (MetdActiveSceneView ?
										MetdActiveCam
										: InGameCam);
			MetdCurrentCam = SecondGate && SecondGate.GetComponent<PortalManager> ().SecondGate && SecondGate.activeSelf ? MetdCurrentCam : null;
		#else
			MetdCurrentCam = InGameCam;
		#endif

		return MetdCurrentCam;
	}

	private Camera CurrentCam;
	private RenderTexture RenTex;
	private Mesh GateMesh;

	void GenGate () {
		//Fill "InGameCam" variable, if its value is null
		if (!InGameCam)
			InGameCam = Camera.main;

		//Generate position/rotation reference object, used by "Clipping Plane" system for slice objects inside the portal
		if (!ClipPlanePosObj) {
			ClipPlanePosObj = new GameObject (transform.name + " ClipPlanePosObj");

			ClipPlanePosObj.transform.position = transform.position;
			ClipPlanePosObj.transform.rotation = transform.rotation;
			ClipPlanePosObj.transform.parent = transform;
		} else {
			if (ClipPlanePosObj.name != transform.name + " ClipPlanePosObj")
				ClipPlanePosObj.name = transform.name + " ClipPlanePosObj";

			//Moving clipping plane in z-axis of objects inside portal
			Vector2 DistClipPlanePosObj = new Vector2 (-3.5f, 4.5f);

			ClipPlanePosObj.transform.localPosition = new Vector3 (0, 0, Vector3.Dot (InGameCam.transform.position - transform.position, transform.forward) < DistClipPlanePosObj.x && Vector3.Distance (InGameCam.transform.position, transform.position) > DistClipPlanePosObj.y ? 0 : 1);
			SecondGate.GetComponent<PortalManager>().ClipPlanePosObj.transform.localPosition = new Vector3 (0, 0, Vector3.Dot (InGameCam.transform.position - transform.position, transform.forward) < DistClipPlanePosObj.x && Vector3.Distance (InGameCam.transform.position, transform.position) > DistClipPlanePosObj.y ? 1 : 0);
		}

		CurrentCam = GetCurrentCam (Camera.current);

		//Generate camera for the portal rendering
		for (int i = 0; i < GateCamObjs.Length; i++) {
			if (i < RecursionSteps) {
				if (!GateCamObjs [i]) {
					GateCamObjs [i] = new GameObject (transform.name + " Camera " + i);

					GateCamObjs [i].tag = "Untagged";

					GateCamObjs [i].transform.parent = transform;
					GateCamObjs [i].AddComponent<Camera> ();
					GateCamObjs [i].GetComponent<Camera> ().enabled = false;
					InitGateCamObjsCullingMaskChecker[i] = false;
					GateCamObjs [i].GetComponent<Camera> ().nearClipPlane = .01f;

					GateCamObjs [i].AddComponent<Skybox> ();

					PreviousProjectionResolution = new Vector2 (0, 0);
				}
				if (GateCamObjs [i]) {
					if (GateCamObjs [i].name != transform.name + " Camera " + i)
						GateCamObjs [i].name = transform.name + " Camera " + i;

					if (!InitGateCamObjsCullingMaskChecker [i]) {
						InitGateCamObjsCullingMask [i] = GateCamObjs [i].GetComponent<Camera> ().cullingMask;

						InitGateCamObjsCullingMaskChecker [i] = true;
					}
					if (InitGateCamObjsCullingMaskChecker [i]) {
						if (CustomRecursionEnd && (i > 0 && i == RecursionSteps - 1))
							GateCamObjs [i].GetComponent<Camera> ().cullingMask = 0;
						else
							GateCamObjs [i].GetComponent<Camera> ().cullingMask = InitGateCamObjsCullingMask [i];
					}
					
					if (CurrentCam == InGameCam && GateCamObjs [i].GetComponent<Camera> ().depth != CurrentCam.depth - 1)
						GateCamObjs [i].GetComponent<Camera> ().depth = CurrentCam.depth - 1;

					//Acquire settings from Scene/Game camera, to apply on Portal camera
					if (CurrentCam) {
						GateCamObjs [i].GetComponent<Camera> ().aspect = CurrentCam.aspect;
						GateCamObjs [i].GetComponent<Camera> ().cullingMask = CurrentCam.cullingMask;
						GateCamObjs [i].GetComponent<Camera> ().cullingMask &=  ~(1 << ExcludedWallsLayer);
						GateCamObjs [i].GetComponent<Camera> ().fieldOfView = CurrentCam.fieldOfView;
						GateCamObjs [i].GetComponent<Camera> ().farClipPlane = CurrentCam.farClipPlane;
						GateCamObjs [i].GetComponent<Camera> ().renderingPath = CurrentCam.renderingPath;
						GateCamObjs [i].GetComponent<Camera> ().useOcclusionCulling = CurrentCam.useOcclusionCulling;
						GateCamObjs [i].GetComponent<Camera> ().hdr = CurrentCam.hdr;
					}
				}

				if (SecondGate && SecondGate.GetComponent<PortalManager> ().GateCamObjs [i])
					SecondGate.GetComponent<PortalManager> ().GateCamObjs [i].GetComponent<Skybox> ().material = CustomRecursionEnd && (i > 0 && i == RecursionSteps - 1) ? CustomRecursionEnd : SecondGateCustomSkybox;
			} else {
				#if UNITY_EDITOR
					if (!EditorApplication.isPlaying)
						DestroyImmediate (GateCamObjs [i], false);
					if (EditorApplication.isPlaying)
						Destroy (GateCamObjs [i]);
				#else
					Destroy(GateCamObjs [i]);
				#endif
			}
		}

		//Generate render texture for the portal camera
		if (PreviousProjectionResolution.x != ProjectionResolution.x || PreviousProjectionResolution.y != ProjectionResolution.y) {
			if (RenTex) {
				#if UNITY_EDITOR
					if (!EditorApplication.isPlaying)
						DestroyImmediate (RenTex, false);
					if (EditorApplication.isPlaying)
						Destroy (RenTex);
				#else
					Destroy(RenTex);
				#endif
			}
			if (!RenTex) {
				RenTex = new RenderTexture (Convert.ToInt32 (ProjectionResolution.x), Convert.ToInt32 (ProjectionResolution.y), 16);
				RenTex.name = this.gameObject.name + " RenderTexture";

				PreviousProjectionResolution = new Vector2 (RenTex.width, RenTex.height);
			}
		}

		//Reset arrays elements of the required objects for teleport, if Scene/Game camera variable is null and any object is still colliding with the portal
		if (!CurrentCam)
			ResetVars (false);

		//Acquire current portal mesh
		GateMesh = GetComponent<MeshFilter> ().sharedMesh;
		//Apply render texture to the portal material
		GetComponent<MeshRenderer> ().sharedMaterial.SetTexture ("_MainTex", CurrentCam && SecondGate.GetComponent<PortalManager> ().RenTex ? SecondGate.GetComponent<PortalManager> ().RenTex : null);

		//Apply current portal mesh to the mesh collider if exist
		if (GetComponent<MeshCollider> ())
			if (GetComponent<MeshCollider> ().sharedMesh != GateMesh)
				GetComponent<MeshCollider> ().sharedMesh = GateMesh;
		//Disable trigger of portal collider
		if (GetComponent<Collider> ()) {
			if (GetComponent<Collider> ().isTrigger != (CurrentCam ? true : false))
				GetComponent<Collider> ().isTrigger = CurrentCam ? (EnablePortalTrigger ? true : false) : false;
		} else
			Debug.LogError ("No collider component found");

		//Check if the excluded objects from trigger have a collider component
		for (int i = 0; i < ExcludedWallsFromTrigger.Length; i++)
			if (ExcludedWallsFromTrigger [i] && !ExcludedWallsFromTrigger [i].GetComponent<Collider> ())
				Debug.LogError ("One excluded wall doesn't have a collider component");
	}

	void GateCamRepos () {
		//Move portal camera to position/rotation of Scene/Game camera
		if (CurrentCam) {
			Vector3[] GateCamPos = new Vector3[GateCamObjs.Length];
			Quaternion[] GateCamRot = new Quaternion[GateCamObjs.Length];

			for (int i = 0; i < RecursionSteps; i++) {
				GateCamPos [i] = SecondGate.transform.InverseTransformPoint (CurrentCam.transform.position);

				GateCamPos [i].x = -GateCamPos [i].x;
				GateCamPos [i].z = -GateCamPos [i].z + i * (Vector3.Distance(transform.position, SecondGate.transform.position) / 5);

				GateCamRot[i] = Quaternion.Inverse (SecondGate.transform.rotation) * CurrentCam.transform.rotation;

				GateCamRot[i] = Quaternion.AngleAxis (180.0f, new Vector3 (0, 1, 0)) * GateCamRot[i];
			}

			RenderTexture TempRenTex = RenderTexture.GetTemporary(Convert.ToInt32 (ProjectionResolution.x), Convert.ToInt32 (ProjectionResolution.y), 16);

			if (RenTex && RenTex != RenderTexture.active)
				RenTex.Release ();

			for (int j = RecursionSteps - 1; j >= 0; j--) {
				GateCamObjs [j].transform.localPosition = GateCamPos[j];
				GateCamObjs [j].transform.localRotation = GateCamRot[j];

				//Render portal camera and recursion to render texture
				if (PreviousProjectionResolution.x == ProjectionResolution.x && PreviousProjectionResolution.y == ProjectionResolution.y) {
					GateCamObjs [j].GetComponent<Camera> ().targetTexture = TempRenTex;

					RenderTexture.active = GateCamObjs [j].GetComponent<Camera> ().targetTexture;

					GateCamObjs [j].GetComponent<Camera> ().Render ();

					RenderTexture.active = null;

					GateCamObjs [j].GetComponent<Camera> ().targetTexture = null;

					Graphics.Blit (TempRenTex, RenTex);
				}
			}

			RenderTexture.ReleaseTemporary (TempRenTex);
		}
	}

	class InitMaterialsList { public Material[] Materials; }
	private GameObject[] CollidedObjs = new GameObject[0];
	private string[] CollidedObjsInitName = new string[0];
	private InitMaterialsList[] CollidedObjsInitMaterials = new InitMaterialsList[0];
	private bool[] StandardObjShader = new bool[0];
	private bool[] CollidedObjsAlwaysTeleport = new bool[0];
	private bool[] CollidedObjsFirstTrig = new bool[0];
	private float[] CollidedObjsFirstTrigDist = new float[0];
	private GameObject[] ProxDetCollidedObjs = new GameObject[0];
	private GameObject[] CloneCollidedObjs = new GameObject[0];
	private Vector3[] CollidedObjVelocity = new Vector3[0];
	private bool[] ContinueTriggerEvents = new bool[0];

	void OnTriggerEnter (Collider collision) {
		int ExcludedObjectsSteps = 0;

		for (int i = 0; i < ExcludedWallsFromTrigger.Length; i++)
			if (collision.gameObject != ExcludedWallsFromTrigger [i])
				ExcludedObjectsSteps += 1;

		//Increment and partially fill the arrays elements of required object for teleport
		if (collision.gameObject != this.gameObject && ExcludedObjectsSteps == ExcludedWallsFromTrigger.Length && !collision.GetComponent<PortalManager> () && !collision.name.Contains (collision.gameObject.GetHashCode ().ToString ()) && !collision.name.Contains ("Clone")) {
			Array.Resize (ref CollidedObjs, CollidedObjs.Length + 1);
			Array.Resize (ref CollidedObjsInitName, CollidedObjsInitName.Length + 1);
			Array.Resize (ref CollidedObjsInitMaterials, CollidedObjsInitMaterials.Length + 1);
			Array.Resize (ref StandardObjShader, StandardObjShader.Length + 1);
			Array.Resize (ref CollidedObjsAlwaysTeleport, CollidedObjsAlwaysTeleport.Length + 1);
			Array.Resize (ref CollidedObjsFirstTrig, CollidedObjsFirstTrig.Length + 1);
			Array.Resize (ref CollidedObjsFirstTrigDist, CollidedObjsFirstTrigDist.Length + 1);
			Array.Resize (ref ProxDetCollidedObjs, ProxDetCollidedObjs.Length + 1);
			Array.Resize (ref CloneCollidedObjs, CloneCollidedObjs.Length + 1);
			Array.Resize (ref CollidedObjVelocity, CollidedObjVelocity.Length + 1);
			Array.Resize (ref ContinueTriggerEvents, ContinueTriggerEvents.Length + 1);

			CollidedObjs [CollidedObjs.Length - 1] = collision.gameObject;
			CollidedObjsInitName [CollidedObjsInitName.Length - 1] = collision.gameObject.name;

			if (ExcludedWallsFromTrigger.Length > 0)
				for (int i = 0; i < ExcludedWallsFromTrigger.Length; i++)
					if (ExcludedWallsFromTrigger [i] && ExcludedWallsFromTrigger [i].GetComponent<Collider> ())
						Physics.IgnoreCollision(CollidedObjs [CollidedObjsInitName.Length - 1].GetComponent<Collider> (), ExcludedWallsFromTrigger [i].GetComponent<Collider> (), true);

			if (CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> ()) {
				if (!StandardObjShader[CollidedObjs.Length - 1]) {
					if (CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> ().sharedMaterial.shader.name != "Standard")
						Debug.LogError ("The shader of object material is not 'Standard', mesh clippping will not be possible");
					if (CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> ().sharedMaterial.shader.name == "Standard")
						StandardObjShader[CollidedObjs.Length - 1] = true;
				}
				if (StandardObjShader[CollidedObjs.Length - 1]) {
					if (CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> () && EnableMeshClipPlane) {
						CollidedObjsInitMaterials [CollidedObjsInitMaterials.Length - 1] = new InitMaterialsList ();

						CollidedObjsInitMaterials [CollidedObjsInitMaterials.Length - 1].Materials = CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> ().sharedMaterials;

						CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> ().sharedMaterial = ClipPlaneMaterial;
						CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> ().sharedMaterial.CopyPropertiesFromMaterial (CollidedObjsInitMaterials [CollidedObjs.Length - 1].Materials [0]);
					}
				}
			}

			ContinueTriggerEvents [ContinueTriggerEvents.Length - 1] = true;
		}
	}

	private GameObject ObjCollidedCamObj = null;
	private GameObject ObjCloneCollidedCamObj = null;

	void OnTriggerStay (Collider collision) {
		//Change position/rotation of required objects for teleport, and complete the fill of remaining arrays elements
		for (int i = 0; i < CollidedObjs.Length; i++) {
			if (ContinueTriggerEvents [i] && CollidedObjs [i]) {
				if (!ProxDetCollidedObjs [i]) {
					ProxDetCollidedObjs [i] = new GameObject (CollidedObjs [i].name + " Proximity Detector");

					ProxDetCollidedObjs [i].transform.position = transform.position;
					ProxDetCollidedObjs [i].transform.rotation = transform.rotation;
					ProxDetCollidedObjs [i].transform.parent = transform;
				}

				bool FirstPersonCharacter;

				if (CollidedObjs [i].transform.childCount > 0)
					for (int j = 0; j < CollidedObjs [i].transform.GetComponentsInChildren<Transform> ().Length; j++)
						if (CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [j].GetComponent<Camera>())
							ObjCollidedCamObj = CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [j].gameObject;

				if (GateMesh && ProxDetCollidedObjs [i]) {
					FirstPersonCharacter = ObjCollidedCamObj && ObjCollidedCamObj.GetComponent<Camera> () && CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> () ? true : false;

					Vector3 ProxDetCollidedObjPos = transform.InverseTransformPoint ((FirstPersonCharacter ? ObjCollidedCamObj.transform.position : CollidedObjs [i].transform.position));
					Vector3 ProxDetCollLimit = new Vector3 (GateMesh.bounds.size.x / 2, GateMesh.bounds.size.y / 2, GateMesh.bounds.size.z / 2);

					ProxDetCollidedObjs [i].transform.localPosition = new Vector3 (ProxDetCollidedObjPos.x > -ProxDetCollLimit.x && ProxDetCollidedObjPos.x < ProxDetCollLimit.x ? ProxDetCollidedObjPos.x : ProxDetCollidedObjs [i].transform.localPosition.x, ProxDetCollidedObjPos.y > -ProxDetCollLimit.y && ProxDetCollidedObjPos.y < ProxDetCollLimit.y ? ProxDetCollidedObjPos.y : ProxDetCollidedObjs [i].transform.localPosition.y, ProxDetCollidedObjs [i].transform.localPosition.z);

					if (!CollidedObjsAlwaysTeleport [i]) {
						if (!CollidedObjsFirstTrig [i]) {
							CollidedObjsFirstTrigDist [i] = Vector3.Dot (CollidedObjs [i].transform.position - ProxDetCollidedObjs [i].transform.position, ProxDetCollidedObjs [i].transform.forward);

							CollidedObjsFirstTrig [i] = true;
						}
						if (CollidedObjsFirstTrig [i] && CollidedObjsFirstTrigDist [i] < 0) {
							CollidedObjsAlwaysTeleport [i] = true;
							
							CollidedObjs [i].name = CollidedObjs [i].name + " " + CollidedObjs [i].GetHashCode ().ToString ();
						}
					}
					if (CollidedObjsAlwaysTeleport [i]) {
						if (!CloneCollidedObjs [i]) {
							CloneCollidedObjs [i] = (GameObject)Instantiate (CollidedObjs [i], SecondGate.transform.position, SecondGate.transform.rotation);
							
							if (CloneCollidedObjs [i].transform.childCount > 0)
								for (int k = 0; k < CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> ().Length; k++)
									if (CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].GetComponent<Camera>())
										ObjCloneCollidedCamObj = CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].gameObject;
							
							if (CloneCollidedObjs [i].GetComponent<MeshRenderer> () && StandardObjShader[i]) {
								if (EnableMeshClipPlane) {
									CloneCollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial = CloneClipPlaneMaterial;
									CloneCollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial.CopyPropertiesFromMaterial (CollidedObjsInitMaterials [i].Materials [0]);
								}
								if (!EnableMeshClipPlane) {
									CollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterials = CollidedObjsInitMaterials [i].Materials;
									CloneCollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterials = CollidedObjsInitMaterials [i].Materials;
								}
							}

							if (ObjCloneCollidedCamObj)
								if (ObjCloneCollidedCamObj.GetComponent<Camera> () && CloneCollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ()) {
									ObjCloneCollidedCamObj.GetComponent<Camera> ().GetComponent<AudioListener> ().enabled = false;
									CloneCollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ().enabled = false;

									CloneCollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ().m_OriginalCameraPosition = CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ().m_OriginalCameraPosition;
								}

							CloneCollidedObjs [i].name = CollidedObjsInitName [i] + " Clone";

							CloneCollidedObjs [i].transform.position = SecondGate.transform.position;
							CloneCollidedObjs [i].transform.parent = SecondGate.transform;
						}
						if (CloneCollidedObjs [i]) {
							float DistAmount = .015f;

							float CollidedObjProxDetDistStay = Vector3.Dot ((FirstPersonCharacter ? ObjCollidedCamObj.transform.position : CollidedObjs [i].transform.position) - ProxDetCollidedObjs [i].transform.position, ProxDetCollidedObjs [i].transform.forward);
							Vector3 CloneCollidedObjLocalPos = transform.InverseTransformPoint (CollidedObjs [i].transform.position);

							CloneCollidedObjLocalPos.x = -CloneCollidedObjLocalPos.x;
							CloneCollidedObjLocalPos.z = -CloneCollidedObjLocalPos.z - DistAmount;

							CloneCollidedObjs [i].transform.localPosition = CloneCollidedObjLocalPos;

							Quaternion CloneCollidedObjLocalRot = Quaternion.Inverse (transform.rotation) * (CollidedObjs [i].transform.rotation);

							CloneCollidedObjLocalRot = Quaternion.AngleAxis (180.0f, new Vector3 (0, -1, 0)) * CloneCollidedObjLocalRot;

							CloneCollidedObjs [i].transform.localRotation = CloneCollidedObjLocalRot;

							if (ObjCollidedCamObj && ObjCloneCollidedCamObj) {
								if (!ObjCloneCollidedCamObj.GetComponent<Skybox> ())
									ObjCloneCollidedCamObj.AddComponent<Skybox> ();
								if (ObjCloneCollidedCamObj.GetComponent<Skybox> ()) {
									ObjCloneCollidedCamObj.GetComponent<Skybox> ().material = SecondGateCustomSkybox;

									if (!SecondGateCustomSkybox) {
										#if UNITY_EDITOR
											if (!EditorApplication.isPlaying)
												DestroyImmediate (ObjCloneCollidedCamObj.GetComponent<Skybox> (), false);
											if (EditorApplication.isPlaying)
												Destroy (ObjCloneCollidedCamObj.GetComponent<Skybox> ());
										#else
											Destroy(ObjCloneCollidedCamObj.GetComponent<Skybox> ());
										#endif
									}
								}

								ObjCollidedCamObj.GetComponent<Camera> ().enabled = CollidedObjProxDetDistStay < -DistAmount ? true : false;
								ObjCloneCollidedCamObj.GetComponent<Camera> ().enabled = CollidedObjProxDetDistStay >= -DistAmount ? true : false;

								InGameCam = ObjCollidedCamObj.GetComponent<Camera> ().enabled ? ObjCollidedCamObj.GetComponent<Camera>() : ObjCloneCollidedCamObj.GetComponent<Camera>();

								ObjCloneCollidedCamObj.transform.localPosition = ObjCollidedCamObj.transform.localPosition;
								ObjCloneCollidedCamObj.transform.localRotation = ObjCollidedCamObj.transform.localRotation;

								if (CurrentCam == InGameCam && CurrentCam.nearClipPlane > .01f) {
									Debug.LogError ("The nearClipPlane of 'Main Camera' is not equal to 0.01");

									#if UNITY_EDITOR
										EditorApplication.isPaused = true;
									#endif
								}
							}

							if (CollidedObjs [i].GetComponent<MeshRenderer> () && CollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial == ClipPlaneMaterial) {
								Vector3 DirectionVector = Vector3.forward;

								CollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial.SetVector ("_planePos", ClipPlanePosObj.transform.position);
								CollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial.SetVector ("_planeNorm", Quaternion.Euler (transform.eulerAngles) * -DirectionVector);

								CloneCollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial.SetVector ("_planePos", SecondGate.GetComponent<PortalManager> ().ClipPlanePosObj.transform.position);
								CloneCollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial.SetVector ("_planeNorm", Quaternion.Euler (SecondGate.transform.eulerAngles) * -DirectionVector);
							}
						}
					}
				}
			}
		}
	}

	void OnTriggerExit (Collider collision) {
		//Destroy required objects for teleport, reset relative arrays, and move original collided object to the its final position/rotation
		for (int i = 0; i < CloneCollidedObjs.Length; i++) {
			if (ContinueTriggerEvents [i] && CollidedObjs [i] && CollidedObjs [i].GetHashCode ().ToString () == collision.gameObject.GetHashCode ().ToString () && CloneCollidedObjs [i]) {
				if (CollidedObjVelocity [i] == new Vector3 (0, 0, 0))
					CollidedObjVelocity [i] = CollidedObjs [i].GetComponent<Rigidbody> () ? CollidedObjs [i].GetComponent<Rigidbody> ().velocity.magnitude * -SecondGate.transform.forward : new Vector3 (0, 0, 0);

				float CollObjProxDetDistExit = Vector3.Dot (CollidedObjs [i].transform.position - ProxDetCollidedObjs [i].transform.position, ProxDetCollidedObjs [i].transform.forward);
				
				if (CollidedObjs [i].transform.childCount > 0)
					for (int j = 0; j < CollidedObjs [i].transform.GetComponentsInChildren<Transform> ().Length; j++)
						if (CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [j].GetComponent<Camera>())
							ObjCollidedCamObj = CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [j].gameObject;
				if (CloneCollidedObjs [i].transform.childCount > 0)
					for (int k = 0; k < CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> ().Length; k++)
						if (CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].GetComponent<Camera>())
							ObjCloneCollidedCamObj = CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].gameObject;
				
				if (CollObjProxDetDistExit > 0) {
					CollidedObjs [i].transform.position = CloneCollidedObjs [i].transform.position;
					CollidedObjs [i].transform.rotation = CloneCollidedObjs [i].transform.rotation;

					if (ObjCollidedCamObj && ObjCloneCollidedCamObj)
						if (ObjCloneCollidedCamObj.GetComponent<Camera> () && CloneCollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ()) {
							if (SecondGateCustomSkybox && ObjCloneCollidedCamObj.GetComponent<Skybox> ())
								if (!ObjCollidedCamObj.GetComponent<Skybox> ())
									ObjCollidedCamObj.AddComponent<Skybox> ();
							if (ObjCollidedCamObj.GetComponent<Skybox> ())		
								ObjCollidedCamObj.GetComponent<Skybox> ().material = SecondGateCustomSkybox;
							
							ObjCollidedCamObj.GetComponent<Camera> ().enabled = true;

							CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ().m_MouseLook.Init (CollidedObjs [i].transform, CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ().m_Camera.transform);
						}

					if (CollidedObjVelocity [i] != new Vector3 (0, 0, 0))
						CollidedObjs [i].GetComponent<Rigidbody> ().velocity = CollidedObjVelocity [i];

					if (ExcludedWallsFromTrigger.Length > 0)
						for (int j = 0; j < ExcludedWallsFromTrigger.Length; j++)
							if (ExcludedWallsFromTrigger [j] && ExcludedWallsFromTrigger [j].GetComponent<Collider> ())
								Physics.IgnoreCollision(CollidedObjs [i].GetComponent<Collider> (), ExcludedWallsFromTrigger [j].GetComponent<Collider> (), false);
				}

				CollidedObjs [i].name = CollidedObjsInitName [i];

				if (CollidedObjs [i].GetComponent<MeshRenderer> () && EnableMeshClipPlane && StandardObjShader[i]) {
					CollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterials = CollidedObjsInitMaterials [i].Materials;

					CollidedObjsInitMaterials [i].Materials = null;
				}

				CollidedObjs [i] = null;
				CollidedObjsInitName [i] = "";
				CollidedObjsAlwaysTeleport [i] = false;
				CollidedObjsFirstTrig [i] = false;
				CollidedObjsFirstTrigDist [i] = 0;
				Destroy (ProxDetCollidedObjs [i]);
				Destroy (CloneCollidedObjs [i]);
				CollidedObjVelocity [i] = new Vector3 (0, 0, 0);
				ContinueTriggerEvents [i] = false;
			}
		}

		ResetVars (true);
	}

	void ResetVars (bool TriggerExit) {
		bool SetVars = false;

		if (CollidedObjs.Length > 0) {
			if (!TriggerExit) {
				for (int i = 0; i < CollidedObjs.Length; i++) {
					if (CloneCollidedObjs [i])
						Destroy (CloneCollidedObjs [i]);
					
					if (ProxDetCollidedObjs [i])
						Destroy (ProxDetCollidedObjs [i]);
					
					if (CollidedObjs [i] && CollidedObjs [i].transform.childCount > 0 && CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ()) {
						Camera CollObjCam = null;

						for (int j = 0; j < CollidedObjs [i].transform.GetComponentsInChildren<Transform> ().Length; j++)
							if (CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [j].GetComponent<Camera>())
								CollObjCam = CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [j].GetComponent<Camera> ();

						if (CollObjCam && !CollObjCam.enabled)
							CollObjCam.enabled = true;
					}
				}

				SetVars = true;
			}
			if (TriggerExit) {
				int ElementsChecked = 0;

				for (int i = 0; i < CollidedObjs.Length; i++)
					if (!CollidedObjs [i])
						ElementsChecked += 1;

				if (ElementsChecked == CollidedObjs.Length)
					SetVars = true;

				ElementsChecked = 0;
			}
		}

		if (SetVars) {
			CollidedObjs = new GameObject[0];
			CollidedObjsInitName = new string[0];
			CollidedObjsInitMaterials = new InitMaterialsList[0];
			CollidedObjsAlwaysTeleport = new bool[0];
			CollidedObjsFirstTrig = new bool[0];
			CollidedObjsFirstTrigDist = new float[0];
			ProxDetCollidedObjs = new GameObject[0];
			CloneCollidedObjs = new GameObject[0];
			CollidedObjVelocity = new Vector3[0];
			ContinueTriggerEvents = new bool[0];
		}
	}
}