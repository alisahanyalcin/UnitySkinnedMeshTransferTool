using System;
using System.Collections.Generic;
using UniHumanoid;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UniVRM10;

#if UNITY_EDITOR
class MeshTransferToolEditor : EditorWindow
{
    public SkinnedMeshRenderer[] skinnedMeshRenderersList;
    public List<SkinnedMeshRenderer> newSkinnedMeshRendererList = new List<SkinnedMeshRenderer>();
    public List<GameObject> sourceMeshList = new List<GameObject>();
    public List<GameObject> deduplicatedMeshList = new List<GameObject>();

    private GameObject _armature;
    private Dictionary<string, Transform> _boneMap = new Dictionary<string, Transform>();
    private ScriptableObject _scriptableObj;
    private SerializedObject _serialObj;
    private bool _deleteSourceGameObjects = false;
    
    [MenuItem("Tools/MeshTransferTool")]
    public static void OpenWindow()
    {
        MeshTransferToolEditor window = GetWindow<MeshTransferToolEditor>();
        window.Show();
    }

    private void OnEnable()
    {
        _scriptableObj = this;
        _serialObj = new SerializedObject(_scriptableObj);
    }
    
    private void GetBones(Transform pBone)
    {
        foreach (Transform bone in pBone)
        {
            _boneMap[bone.gameObject.name] = bone;
            GetBones(bone);
        }
    }
    
    private void BoneRemap(SkinnedMeshRenderer mesh)
    {
        Transform[] newBonesList = new Transform[mesh.bones.Length];
        for (int j = 0; j < mesh.bones.Length; ++j)
        {
            Transform tempBone = mesh.bones[j];
            if (tempBone == null)
            {
                continue;
            }

            GameObject bone = tempBone.gameObject;
            if (!_boneMap.TryGetValue(bone.name, out newBonesList[j])) // Check to see if bone exists in bone map
            {
                if (_boneMap.TryGetValue(bone.transform.parent.name, out Transform pBone)) // try to find the parent reference in the target armature
                {
                    bone.transform.position += pBone.position - bone.transform.parent.position;
                    bone.transform.SetParent(pBone);
                    GetBones(pBone.transform);
                    _boneMap.TryGetValue(bone.name, out newBonesList[j]);
                }
                else
                {
                    Debug.Log("Unable to map bone \"" + bone.name + "\" to target skeleton.");
                }
            }
        }

        mesh.bones = newBonesList;
    }

    private void TransferMeshes(SkinnedMeshRenderer mesh)
    {
        mesh.gameObject.transform.SetParent(_armature.gameObject.transform.parent);
        mesh.rootBone = _armature.transform;
    }

    public void OnGUI()
    {
        GUILayout.Label("Remap Meshes", EditorStyles.largeLabel);

        GUILayout.Space(10f);

        GUILayout.Label("Target Skeleton:", EditorStyles.boldLabel);
        _armature = (GameObject)EditorGUILayout.ObjectField(_armature, typeof(GameObject), true, GUILayout.Height(25f));

        GUILayout.Space(20f);
        
        GUILayout.Label("Skinned Meshes:", EditorStyles.boldLabel);
        var serialProp = _serialObj.FindProperty("skinnedMeshRenderersList");
        EditorGUILayout.PropertyField(serialProp, true);
        _serialObj.ApplyModifiedProperties();
        
        GUILayout.Space(20f);
        // delete source game object bool field
        _deleteSourceGameObjects = EditorGUILayout.Toggle("Delete source game object", _deleteSourceGameObjects);
        // make info panel => this tool already deduplicate source game objects you don't need to keep source game objects
        if (!_deleteSourceGameObjects)
        {
            GUILayout.Label("This tool already deduplicate source game objects you don't need to keep source game objects", EditorStyles.helpBox);
        }

        GUILayout.Space(10f);

        if (GUILayout.Button("Remap Meshes", GUILayout.Height(25f)))
        {
            // get armature root
            GameObject armatureRoot = _armature.gameObject.transform.root.gameObject;
            
            // get root game object humanoid component
            Humanoid humanoidComponent = armatureRoot.GetComponent<Humanoid>();
            
            // find root game object secondary game object
            //SerializedObject sourceObject = new SerializedObject(humanoidComponent);
            var armatureSecondary = armatureRoot.transform.Find("secondary");
            VRM10SpringBoneColliderGroup[] armatureSecondaryColliderGroups = armatureSecondary.GetComponents<VRM10SpringBoneColliderGroup>();

            foreach (var skinnedMeshRenderer in skinnedMeshRenderersList)
            {
                // get skinned mesh renderer root
                GameObject skinnedMeshRendererRoot = skinnedMeshRenderer.gameObject.transform.root.gameObject;
                // deduplicate "skinnedMeshRendererRoot" and hide it
                var deduplicatedMesh = Instantiate(skinnedMeshRendererRoot);
                deduplicatedMesh.name = skinnedMeshRendererRoot.name;
                deduplicatedMesh.SetActive(false);
                deduplicatedMeshList.Add(deduplicatedMesh);

                sourceMeshList.Add(skinnedMeshRendererRoot);
                var vrmInstance = skinnedMeshRendererRoot.GetComponent<Vrm10Instance>();
                
                List<VRM10SpringBoneColliderGroup> springBoneColliderGroups = new List<VRM10SpringBoneColliderGroup>();
                springBoneColliderGroups.AddRange(vrmInstance.SpringBone.ColliderGroups);
                vrmInstance.SpringBone.ColliderGroups.Clear();
                
                // crete new game object in "armatureRoot" add skinned mesh renderer
                GameObject newGameObject = new GameObject
                {
                    name = skinnedMeshRenderer.name
                };
                newGameObject.transform.SetParent(skinnedMeshRendererRoot.transform);
                
                // copy skinned mesh renderer to new game object
                SkinnedMeshRenderer newSkinnedMeshRenderer = newGameObject.AddComponent<SkinnedMeshRenderer>();
                ComponentUtility.CopyComponent(skinnedMeshRenderer);
                ComponentUtility.PasteComponentValues(newSkinnedMeshRenderer);
                
                var humanoid = newSkinnedMeshRenderer.gameObject.AddComponent<Humanoid>();
                SerializedObject targetHumanoidObject = new SerializedObject(humanoid);
                
                var addVrmInstance = newSkinnedMeshRenderer.gameObject.AddComponent<Vrm10Instance>();
                SerializedObject targetVrmObject = new SerializedObject(humanoid);
                
                ComponentUtility.CopyComponent(humanoidComponent);
                ComponentUtility.PasteComponentValues(humanoid);
                targetHumanoidObject.ApplyModifiedPropertiesWithoutUndo();
                
                // add mew skinned mesh renderer to "newSkinnedMeshRendererList"
                newSkinnedMeshRendererList.Add(newSkinnedMeshRenderer);

                List<Vrm10InstanceSpringBone.Spring> springsToRemove = new List<Vrm10InstanceSpringBone.Spring>();

                foreach (var spring in vrmInstance.SpringBone.Springs)
                {
                    if (spring.Name == String.Empty || spring.Name == "Hair" || spring.Name == "hair")
                    {
                        spring.ColliderGroups.Clear();
                        foreach (var armatureSecondaryColliderGroup in armatureSecondaryColliderGroups)
                        {
                            spring.ColliderGroups.Add(armatureSecondaryColliderGroup);
                        }
                    }
                    else
                    {
                        // add list unnecessary springs
                        springsToRemove.Add(spring);
                    }
                }

                // remove unnecessary springs
                foreach (var springToRemove in springsToRemove)
                {
                    vrmInstance.SpringBone.Springs.Remove(springToRemove);
                }
                
                ComponentUtility.CopyComponent(vrmInstance);
                ComponentUtility.PasteComponentValues(addVrmInstance);
                targetVrmObject.ApplyModifiedPropertiesWithoutUndo();
            }
            MeshRemap();
        }
    }

    private void MeshRemap()
    {
        if (_armature == null)
        {
            Debug.Log("Target Skeleton is not assigned.");
            return;
        }
        
        GetBones(_armature.transform);

        foreach (var mesh in newSkinnedMeshRendererList)
        {
            if (mesh == null)
                continue;

            BoneRemap(mesh);
            TransferMeshes(mesh);
            
            if (_deleteSourceGameObjects)
            {
                DeleteSource();
            }
        }
    }

    private void DeleteSource()
    {
        foreach (var sourceMesh in sourceMeshList)
        {
            DestroyImmediate(sourceMesh);
        }

        foreach (var deduplicatedMesh in deduplicatedMeshList)
        {
            deduplicatedMesh.SetActive(true);
        }
    }
}
#endif