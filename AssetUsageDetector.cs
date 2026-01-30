using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityAssetUsageChecker
{
    public class AssetUsageDetector : EditorWindow
    {
        private Object selectedObject;
        
        // Active Scene Results
        private List<GameObject> activeSceneReferences = new List<GameObject>();
        
        // Project Asset Results
        private List<string> projectAssetReferences = new List<string>();
        
        // Global Scene Results (Scene Path -> List of Hierarchy Paths)
        private Dictionary<string, List<string>> globalSceneReferences = new Dictionary<string, List<string>>();

        private Vector2 scrollPos;
        private bool scanProjectAssets = false;
        private bool scanAllScenes = false;

        [MenuItem("Tools/Asset Usage Detector")]
        public static void ShowWindow()
        {
            GetWindow<AssetUsageDetector>("Asset Usage Detector");
        }

        private void OnEnable()
        {
            OnSelectionChange();
        }

        private void OnSelectionChange()
        {
            Object newSelection = Selection.activeObject;
            if (newSelection != selectedObject)
            {
                selectedObject = newSelection;
                // Auto-scan active scene only to be fast
                if (selectedObject != null)
                {
                    ScanActiveScene();
                }
                Repaint();
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("Asset Usage Detector", EditorStyles.boldLabel);

            if (selectedObject == null)
            {
                EditorGUILayout.HelpBox("Select an asset or object to see usages.", MessageType.Info);
                return;
            }

            EditorGUILayout.ObjectField("Selected Asset", selectedObject, typeof(Object), true);

            GUILayout.Space(10);
            
            GUILayout.BeginHorizontal();
            scanProjectAssets = EditorGUILayout.ToggleLeft("Scan Assets", scanProjectAssets, GUILayout.Width(100));
            scanAllScenes = EditorGUILayout.ToggleLeft("Scan All Scenes", scanAllScenes, GUILayout.Width(120));
            if (GUILayout.Button("Full Scan"))
            {
                RunFullScan();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // 1. Active Scene References
            DrawSectionHeader($"Active Scene References ({activeSceneReferences.Count})", SceneManager.GetActiveScene().name);
            if (activeSceneReferences.Count > 0)
            {
                foreach (var go in activeSceneReferences)
                {
                    if (go == null) continue;
                    DrawReferenceRow(() => {
                        Selection.activeGameObject = go;
                        EditorGUIUtility.PingObject(go);
                    }, GetHierarchyPath(go.transform));
                }
            }
            else
            {
                GUILayout.Label("No references found in Active Scene.");
            }

            GUILayout.Space(10);

            // 2. Global Scene References
            if (scanAllScenes)
            {
                int totalCount = 0;
                foreach(var list in globalSceneReferences.Values) totalCount += list.Count;

                DrawSectionHeader($"Global Scene References ({totalCount})", "Other Scenes");
                
                if (globalSceneReferences.Count > 0)
                {
                    foreach (var kvp in globalSceneReferences)
                    {
                        string scenePath = kvp.Key;
                        List<string> paths = kvp.Value;
                        
                        EditorGUILayout.LabelField(System.IO.Path.GetFileNameWithoutExtension(scenePath), EditorStyles.boldLabel);
                        
                        foreach (var hierarchyPath in paths)
                        {
                            DrawReferenceRow(() => {
                                OpenSceneAndSelect(scenePath, hierarchyPath);
                            }, hierarchyPath);
                        }
                    }
                }
                else
                {
                    GUILayout.Label("No references found in other scenes.");
                }
                GUILayout.Space(10);
            }

            // 3. Project Asset References
            if (scanProjectAssets)
            {
                DrawSectionHeader($"Project Asset References ({projectAssetReferences.Count})", "Prefabs/Scriptables");
                if (projectAssetReferences.Count > 0)
                {
                    foreach (var path in projectAssetReferences)
                    {
                        DrawReferenceRow(() => {
                            Object obj = AssetDatabase.LoadMainAssetAtPath(path);
                            Selection.activeObject = obj;
                            EditorGUIUtility.PingObject(obj);
                        }, path);
                    }
                }
                else
                {
                    GUILayout.Label("No references found in project assets.");
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSectionHeader(string title, string subtitle)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(subtitle, EditorStyles.miniLabel);
            EditorGUILayout.Separator();
        }

        private void DrawReferenceRow(System.Action onSelect, string label)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select", GUILayout.Width(50)))
            {
                onSelect?.Invoke();
            }
            GUILayout.Label(label);
            GUILayout.EndHorizontal();
        }

        private void RunFullScan()
        {
            activeSceneReferences.Clear();
            projectAssetReferences.Clear();
            globalSceneReferences.Clear();

            if (selectedObject == null) return;

            // Always scan active scene
            ScanActiveScene();

            if (scanAllScenes)
            {
                if (EditorUtility.DisplayDialog("Scan All Scenes", 
                    "Scanning all scenes requires saving the current scene and opening every scene in the project. This may take time.\n\nContinue?", "Yes", "No"))
                {
                    ScanAllScenes();
                }
            }

            if (scanProjectAssets)
            {
                ScanProjectAssets();
            }
        }

        private void ScanActiveScene()
        {
            GameObject[] rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var root in rootObjects)
            {
                CheckGameObjectRecursively(root, (go) => {
                    if (!activeSceneReferences.Contains(go))
                        activeSceneReferences.Add(go);
                });
            }
        }

        private void ScanAllScenes()
        {
            string originalScenePath = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(originalScenePath))
            {
                // Current scene might not be saved
                 if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                 originalScenePath = SceneManager.GetActiveScene().path;
            }
            else
            {
                 EditorSceneManager.SaveOpenScenes();
            }

            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
            float count = 0;
            float total = sceneGuids.Length;

            foreach (var guid in sceneGuids)
            {
                count++;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                
                if (EditorUtility.DisplayCancelableProgressBar("Scanning Scenes", $"Scanning {System.IO.Path.GetFileName(path)}...", count / total))
                {
                    break;
                }

                if (path == originalScenePath) continue; // Already scanned as active

                try 
                {
                    EditorSceneManager.OpenScene(path);
                    
                    List<string> foundPaths = new List<string>();
                    GameObject[] rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
                    foreach (var root in rootObjects)
                    {
                        CheckGameObjectRecursively(root, (go) => {
                            foundPaths.Add(GetHierarchyPath(go.transform));
                        });
                    }

                    if (foundPaths.Count > 0)
                    {
                        globalSceneReferences.Add(path, foundPaths);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Failed to scan scene '{path}': {e.Message}");
                }
            }

            EditorUtility.ClearProgressBar();

            // Restore original
            if (!string.IsNullOrEmpty(originalScenePath))
                EditorSceneManager.OpenScene(originalScenePath);
        }

        private void CheckGameObjectRecursively(GameObject go, System.Action<GameObject> onFound)
        {
            bool found = false;

            // Check Components
            Component[] components = go.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                
                SerializedObject so = new SerializedObject(comp);
                SerializedProperty sp = so.GetIterator();

                while (sp.NextVisible(true))
                {
                    if (sp.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        if (sp.objectReferenceValue == selectedObject)
                        {
                            found = true;
                            break;
                        }
                    }
                }
                if (found) break;
            }

            if (!found && PrefabUtility.GetCorrespondingObjectFromSource(go) == selectedObject)
            {
                found = true;
            }

            if (found)
            {
                onFound(go);
            }

            foreach (Transform child in go.transform)
            {
                CheckGameObjectRecursively(child.gameObject, onFound);
            }
        }

        private void ScanProjectAssets()
        {
            string selectedPath = AssetDatabase.GetAssetPath(selectedObject);
            if (string.IsNullOrEmpty(selectedPath)) return;

            string[] allAssets = AssetDatabase.GetAllAssetPaths();
            foreach (var path in allAssets)
            {
                if (path == selectedPath) continue;

                string[] dependencies = AssetDatabase.GetDependencies(path, false);
                foreach (var dep in dependencies)
                {
                    if (dep == selectedPath)
                    {
                        projectAssetReferences.Add(path);
                        break;
                    }
                }
            }
        }

        private void OpenSceneAndSelect(string scenePath, string hierarchyPath)
        {
            if (SceneManager.GetActiveScene().path != scenePath)
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    EditorSceneManager.OpenScene(scenePath);
                }
                else
                {
                    return; 
                }
            }

            GameObject foundGo = GameObject.Find(hierarchyPath);
            if (foundGo != null)
            {
                Selection.activeGameObject = foundGo;
                EditorGUIUtility.PingObject(foundGo);
            }
            else
            {
                Debug.LogWarning("Could not find object at path: " + hierarchyPath);
            }
        }

        private string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }
    }
}
