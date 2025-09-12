using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking; // para hacer los requests a flask

[System.Serializable]
public class RescatistaData {
    public int id;
    public int x;
    public int y;
    public int victimsRescued;
    public bool hasVictim;
    public int moves;
}

[System.Serializable]
public class ObstacleData
{
    public int x;
    public int y;
    public string direction; //1=N, 2=E, 4=S, 8=W
    public int type; // 1 pared, 2 puerta, 3 puerta abierta
}


[System.Serializable]
public class ZombieData {
    public int x;
    public int y;
    public int state; // 1 = sleeping, 2 = active, 3 = killer
}

[System.Serializable]
public class VictimData {
    public int x;
    public int y;
    public int type; // 1 unrevealed, 2 victim, 3 false alarm
}

[System.Serializable]
public class StepData {
    public RescatistaData[] rescatistas;
    public VictimData[] victims;
    public ZombieData[] zombies;
    public ObstacleData[] obstacles;
}

public class APIClient : MonoBehaviour {
    public string url = "http://127.0.0.1:5000/step";

    // Prefabs para instanciar en Unity
    public GameObject rescatistaPrefab;
    public GameObject victimPrefab;
    public GameObject poiPrefab;
    public GameObject zombieSleepingPrefab;
    public GameObject zombieActivePrefab;
    public GameObject zombieKillerPrefab;
    public GameObject doorPrefab;

    // Diccionarios en lugar de listas para no duplicar objetos
    private Dictionary<int, GameObject> rescatistasGO = new Dictionary<int, GameObject>();
    private Dictionary<string, GameObject> doorsGO = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> zombiesGO = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> victimsGO = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> wallsGO = new Dictionary<string, GameObject>();

    // === Configuración de grid ===
    public float cellSize = 1.0f;   // tamaño de cada celda en Unity
    public float offsetX = 48f;   // offset en X (ajustar según el escenario)
    public float offsetZ = -71f;   // offset en Z (ajustar según el escenario)

     public float offsetY = -0.5f; 
    void Start() {
        // Buscar paredes ya colocadas en la escena
        foreach (var wall in GameObject.FindGameObjectsWithTag("Wall")) {
        string cleanName = wall.name
            .Replace("Wall", "")   // quita la palabra Wall
            .Replace("(", "")      // quita paréntesis
            .Replace(")", "")      // quita paréntesis
            .Replace(" ", "");     // quita espacios
        wallsGO[cleanName] = wall;
        }
        
        // Buscar puertas ya colocadas en la escena
        foreach (var door in GameObject.FindGameObjectsWithTag("Door"))
        {
            string cleanName = door.name
                .Replace("Door", "")   // quita la palabra Door
                .Replace("(", "")
                .Replace(")", "")
                .Replace(" ", "");
            doorsGO[cleanName] = door;
        }
        StartCoroutine(UpdateLoop());
    }

    IEnumerator UpdateLoop() {
        while (true) {
            yield return GetStepData();
            yield return new WaitForSeconds(1f); // refrescar cada 1s
        }
    }

    IEnumerator GetStepData() {
        UnityWebRequest request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success) {
            StepData stepData = JsonUtility.FromJson<StepData>(request.downloadHandler.text);
            RefreshState(stepData);
        } else {
            Debug.LogError("Error al conectarse a Flask: " + request.error);
        }
    }

    void RefreshState(StepData data) {
        // === Rescatistas ===
        HashSet<int> rescatistasAlive = new HashSet<int>();
        foreach (var r in data.rescatistas) {
            rescatistasAlive.Add(r.id);
            Vector3 pos = ToWorldPosition(r.x, r.y);

            if (!rescatistasGO.ContainsKey(r.id)) {
                GameObject resc = Instantiate(rescatistaPrefab, pos, Quaternion.identity);
                rescatistasGO[r.id] = resc;
            } else {
                rescatistasGO[r.id].transform.position = pos;
            }
        }
        // eliminar rescatistas que ya no estén
        List<int> rescToRemove = new List<int>();
        foreach (var id in rescatistasGO.Keys)
        {
            if (!rescatistasAlive.Contains(id))
                rescToRemove.Add(id);

        }
        foreach (var id in rescToRemove)
        {
            Destroy(rescatistasGO[id]);
            rescatistasGO.Remove(id);
        }

        // === Paredes (type = 1) ===
        HashSet<string> wallsAlive = new HashSet<string>();
        foreach (var o in data.obstacles) {
            if (o.type != 1) continue; // solo paredes

            string key = $"{o.x},{o.y},{o.direction}";
            wallsAlive.Add(key);

            if (wallsGO.ContainsKey(key)) {
                wallsGO[key].SetActive(true); // activar pared si existe en el server
            }
        }

        // Desactivar paredes que ya no estén
        foreach (var key in wallsGO.Keys) {
            if (!wallsAlive.Contains(key)) {
                wallsGO[key].SetActive(false);
            }
        }
    
        // === Puertas (type = 2 = cerrada, type = 3 = abierta) ===
        HashSet<string> doorsAlive = new HashSet<string>();
        foreach (var o in data.obstacles) {
            if (o.type != 2 && o.type != 3) continue; 

            string key = $"{o.x},{o.y},{o.direction}";
            doorsAlive.Add(key);

            if (doorsGO.ContainsKey(key)) {
                var door = doorsGO[key];
                door.SetActive(true);

                // Rotar si está abierta
                if (o.type == 3) {
                    door.transform.rotation = Quaternion.Euler(0, 90, 0); 
                } else {
                    door.transform.rotation = Quaternion.identity;
                }
            }
        }



        // === Zombies ===
        HashSet<string> zombiesAlive = new HashSet<string>();
        foreach (var z in data.zombies)
        {
            string key = $"{z.x},{z.y}";
            zombiesAlive.Add(key);

            Vector3 pos = ToWorldPosition(z.x, z.y);

            GameObject prefab = zombieSleepingPrefab;
            if (z.state == 2) prefab = zombieActivePrefab;
            else if (z.state == 3) prefab = zombieKillerPrefab;

            if (!zombiesGO.ContainsKey(key))
            {
                GameObject go = Instantiate(prefab, pos, Quaternion.identity);
                zombiesGO[key] = go;
            }
            else
            {
                zombiesGO[key].transform.position = pos;

                // Si cambió de estado (ej: de dormido a killer), reemplazar prefab
                string currentPrefabName = zombiesGO[key].name.Replace("(Clone)", "");
                if (prefab.name != currentPrefabName)
                {
                    Destroy(zombiesGO[key]);
                    GameObject go = Instantiate(prefab, pos, Quaternion.identity);
                    zombiesGO[key] = go;
                }
            }
        }

        // Limpiar zombies que ya no existen
        List<string> zombiesToRemove = new List<string>();
        foreach (var key in zombiesGO.Keys)
        {
            if (!zombiesAlive.Contains(key))
                zombiesToRemove.Add(key);
        }
        foreach (var key in zombiesToRemove)
        {
            Destroy(zombiesGO[key]);
            zombiesGO.Remove(key);
        }


        // === Victims ===
        HashSet<string> victimsAlive = new HashSet<string>();
        foreach (var v in data.victims)
        {
            string key = $"{v.x},{v.y}";
            victimsAlive.Add(key);

            Vector3 pos = ToWorldPosition(v.x, v.y);

            if (v.type == 3)
            {
                // Falsa alarma -> eliminar si existe
                if (victimsGO.ContainsKey(key))
                {
                    Destroy(victimsGO[key]);
                    victimsGO.Remove(key);
                }
            }
            else if (v.type == 2)
            {
                // Víctima real
                if (!victimsGO.ContainsKey(key))
                {
                    GameObject go = Instantiate(victimPrefab, pos, Quaternion.identity);
                    victimsGO[key] = go;
                }
                else
                {
                    victimsGO[key].transform.position = pos;
                }
            }
            else if (v.type == 1)
            {
                // POI sin revelar
                if (!victimsGO.ContainsKey(key))
                {
                    GameObject go = Instantiate(poiPrefab, pos, Quaternion.identity);
                    victimsGO[key] = go;
                }
                else
                {
                    victimsGO[key].transform.position = pos;
                }
            }

        }

        // Limpiar los que ya no están
        List<string> toRemove = new List<string>();
        foreach (var key in victimsGO.Keys)
        {
            if (!victimsAlive.Contains(key))
                toRemove.Add(key);
        }
        foreach (var key in toRemove)
        {
            Destroy(victimsGO[key]);
            victimsGO.Remove(key);
        }



    }

    void RefreshObjects<T>(T[] objects, GameObject prefab, Dictionary<string, GameObject> dict, System.Func<T, string> getKey) {
        HashSet<string> alive = new HashSet<string>();
        foreach (var obj in objects) {
            string key = getKey(obj);
            alive.Add(key);
            var (pos, rot) = GetTransform(obj);

            if (!dict.ContainsKey(key)) {
                GameObject go = Instantiate(prefab, pos, rot);
                dict[key] = go;
            } else {
                dict[key].transform.position = pos;
                dict[key].transform.rotation = rot;
            }
        }

        // Eliminar objetos que ya no están
        List<string> toRemove = new List<string>();
        foreach (var key in dict.Keys) {
            if (!alive.Contains(key))
                toRemove.Add(key);
        }
        foreach (var key in toRemove) {
            Destroy(dict[key]);
            dict.Remove(key);
        }
    }

    (Vector3, Quaternion) GetTransform<T>(T obj) {
         if (obj is ObstacleData o && o.type == 2) { // solo puertas
            Vector3 basePos = ToWorldPosition(o.x, o.y);
            Vector3 offset = Vector3.zero;
            Quaternion rot = Quaternion.identity;

            float half = cellSize / 2f;

            if (o.direction == "N") {
                offset = new Vector3(0, 0, half);
                rot = Quaternion.identity;
            }
            else if (o.direction == "E") {
                offset = new Vector3(half, 0, 0);
                rot = Quaternion.Euler(0, 90, 0);
            }
            else if (o.direction == "S") {
                offset = new Vector3(0, 0, -half);
                rot = Quaternion.identity;
            }
            else if (o.direction == "W") {
                offset = new Vector3(-half, 0, 0);
                rot = Quaternion.Euler(0, 90, 0);
            }

            return (basePos + offset, rot);
        }

        if (obj is ZombieData z)
            return (ToWorldPosition(z.x, z.y), Quaternion.identity);

        if (obj is VictimData v)
            return (ToWorldPosition(v.x, v.y), Quaternion.identity);

        return (Vector3.zero, Quaternion.identity);
    }



    //traducir coordenadas de Mesa → Unity
    Vector3 ToWorldPosition(int gridX, int gridY) {
        float worldX = gridX * cellSize + offsetX;
        float worldZ = gridY * cellSize + offsetZ;
        return new Vector3(worldX, offsetY, worldZ);
    }
}









