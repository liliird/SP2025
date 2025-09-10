using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking; // para hacer los requests a flask

[System.Serializable]
public class RescatistaData {
    public int id;
    public int x;
    public int y;
    public int victimsRescue;
    public bool hasVictim;
    public int moves;
}

[System.Serializable]
public class ObstacleData
{
    public int x;
    public int y;
    public string direction; //1=N, 2=E, 4=S, 8=W
    public int type; // 1 pared, 2 puerta
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
    public GameObject doorPrefab;
    public GameObject wallPrefab;
    public GameObject zombiePrefab;
    public GameObject victimPrefab;

    // Diccionarios en lugar de listas para no duplicar objetos
    private Dictionary<int, GameObject> rescatistasGO = new Dictionary<int, GameObject>();
    private Dictionary<string, GameObject> obstaclesGO = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> zombiesGO = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> victimsGO = new Dictionary<string, GameObject>();

    // === Configuración de grid ===
    public float cellSize = 1.0f;   // tamaño de cada celda en Unity
    public float offsetX = 48f;   // offset en X (ajustar según el escenario)
    public float offsetZ = -71f;   // offset en Z (ajustar según el escenario)

    void Start() {
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

        // === Obstacles (walls + doors) ===
        HashSet<string> obstaclesAlive = new HashSet<string>();
        foreach (var o in data.obstacles) {
            string key = $"{o.x},{o.y},{o.direction}";
            obstaclesAlive.Add(key);

            var (pos, rot) = GetTransform(o);

            GameObject prefabToUse = null;

            if (o.type == 1) {
                prefabToUse = wallPrefab;   // prefab de MURO
            }
            else if (o.type == 2) {
                prefabToUse = doorPrefab;   // prefab de PUERTA
            }

            if (!obstaclesGO.ContainsKey(key)) {
                GameObject go = Instantiate(prefabToUse, pos, rot);
                obstaclesGO[key] = go;
            } else {
                obstaclesGO[key].transform.position = pos;
                obstaclesGO[key].transform.rotation = rot;
            }
        }

        // 🔴 Eliminar obstáculos que ya no están
        List<string> toRemove = new List<string>();
        foreach (var key in obstaclesGO.Keys) {
            if (!obstaclesAlive.Contains(key))
                toRemove.Add(key);
        }
        foreach (var key in toRemove) {
            Destroy(obstaclesGO[key]);
            obstaclesGO.Remove(key);
        }


        

        // === Zombies ===
        RefreshObjects<ZombieData>(data.zombies, zombiePrefab, zombiesGO, z => $"{z.x},{z.y}");

        // === Victims ===
        RefreshObjects<VictimData>(data.victims, victimPrefab, victimsGO, v => $"{v.x},{v.y}");
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
        if (obj is ObstacleData o) {
            Vector3 basePos = ToWorldPosition(o.x, o.y);
            Vector3 offset = Vector3.zero;
            Quaternion rot = Quaternion.identity;

            float half = cellSize / 2f;

            if (o.direction == "N")
            { // NORTH
                offset = new Vector3(0, 0, half);
                rot = Quaternion.identity;
            }
            else if (o.direction == "E")
            { // EAST
                offset = new Vector3(half, 0, 0);
                rot = Quaternion.Euler(0, 90, 0);
            }
            else if (o.direction == "S")
            { // SOUTH
                offset = new Vector3(0, 0, -half);
                rot = Quaternion.identity;
            }
            else if (o.direction == "W")
            { // WEST
                offset = new Vector3(-half, 0, 0);
                rot = Quaternion.Euler(0, 90, 0);
            }

            GameObject prefab = (o.type == 1) ? wallPrefab : doorPrefab;
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
        return new Vector3(worldX, 0, worldZ);
    }
}


