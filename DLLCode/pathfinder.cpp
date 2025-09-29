#include <vector>
#include <queue>
#include <cmath>
#include <unordered_map>
#include <algorithm>
#include <set>

#define DLLEXPORT extern "C" __declspec(dllexport)

// 3D 정수 벡터 구조체 (해시 함수 및 비교 연산자 추가)
struct Vec3Int {
    int x, y, z;
    bool operator==(const Vec3Int& other) const { return x == other.x && y == other.y && z == other.z; }
    bool operator!=(const Vec3Int& other) const { return !(*this == other); }
    bool operator<(const Vec3Int& other) const {
        if (x != other.x) return x < other.x;
        if (y != other.y) return y < other.y;
        return z < other.z;
    }
};

// Vec3Int를 unordered_map의 키로 사용하기 위한 해시 함수
namespace std {
    template <> struct hash<Vec3Int> {
        size_t operator()(const Vec3Int& v) const {
            return ((hash<int>()(v.x) ^ (hash<int>()(v.y) << 1)) >> 1) ^ (hash<int>()(v.z) << 1);
        }
    };
}

// 3D 실수 벡터 구조체
struct Vec3 { float x, y, z; };

// A* 알고리즘에서 사용할 노드 구조체
struct Node {
    Vec3Int pos;
    float gCost;
    float fCost;
    Node* parent;
    int direction_idx;
    int stepsSinceBend;

    bool operator>(const Node& other) const { return fCost > other.fCost; }
};

// --- 전역 변수 ---
static std::vector<int> g_costGrid;
static int g_countX, g_countY, g_countZ;
static Vec3 g_minBounds;
static float g_gridSize;
const int MAX_DISTANCE = 99999;
const int OBSTACLE_THRESHOLD = 10000;
const Vec3Int DIRECTIONS[6] = { {1,0,0}, {-1,0,0}, {0,1,0}, {0,-1,0}, {0,0,1}, {0,0,-1} };
static std::vector<int> g_distanceToObstacleGrid; // (신규) 장애물까지의 거리를 저장할 그리드

// --- 유틸리티 함수 ---
int GetIndex(const Vec3Int& v) { return v.x + g_countX * (v.y + g_countY * v.z); }
bool IsValid(const Vec3Int& v) { return v.x >= 0 && v.x < g_countX && v.y >= 0 && v.y < g_countY && v.z >= 0 && v.z < g_countZ; }
float Heuristic(const Vec3Int& a, const Vec3Int& b) { return static_cast<float>(std::abs(a.x - b.x) + std::abs(a.y - b.y) + std::abs(a.z - b.z)); }

Vec3Int WorldToGrid(const Vec3& worldPos) {
    return {
        static_cast<int>(floor((worldPos.x - g_minBounds.x) / g_gridSize)),
        static_cast<int>(floor((worldPos.y - g_minBounds.y) / g_gridSize)),
        static_cast<int>(floor((worldPos.z - g_minBounds.z) / g_gridSize))
    };
}
Vec3 GridToWorld(const Vec3Int& gridPos) {
    return {
        g_minBounds.x + (gridPos.x + 0.5f) * g_gridSize,
        g_minBounds.y + (gridPos.y + 0.5f) * g_gridSize,
        g_minBounds.z + (gridPos.z + 0.5f) * g_gridSize
    };
}

/**
 * @brief (Hard Constraint) 파이프가 들어갈 필수 공간이 확보되는지 확인합니다.
 * @return 공간이 있으면 true, 없으면 false (탐색 불가)
 */
bool IsSpaceAvailable(const Vec3Int& pos, int radius, int clearance) {
    int checkRadius = radius + clearance;
    if (checkRadius <= 0) return true;
    for (int x = -checkRadius; x <= checkRadius; ++x) {
        for (int y = -checkRadius; y <= checkRadius; ++y) {
            for (int z = -checkRadius; z <= checkRadius; ++z) {
                if (x*x + y*y + z*z > checkRadius * checkRadius) continue;
                Vec3Int checkPos = { pos.x + x, pos.y + y, pos.z + z };
                if (!IsValid(checkPos) || g_costGrid[GetIndex(checkPos)] >= OBSTACLE_THRESHOLD) {
                    return false;
                }
            }
        }
    }
    return true;
}

/**
 * @brief (Soft Constraint) 장애물 또는 고비용 지역과의 거리에 따라 추가 비용을 계산합니다.
 * @return 거리에 반비례하는 추가 비용
 */
float CalculateProximityCost(const Vec3Int& pos, int radius, int clearance) {
    float proximityCost = 0.0f;
    // 필수 공간(radius+clearance)보다 더 넓은 영역을 탐색하여 '선호하는' 경로를 찾음
    int proximityRadius = radius + clearance + 2; // 예: 2칸 더 넓게 탐색
    
    for (int x = -proximityRadius; x <= proximityRadius; ++x) {
        for (int y = -proximityRadius; y <= proximityRadius; ++y) {
            for (int z = -proximityRadius; z <= proximityRadius; ++z) {
                if (x == 0 && y == 0 && z == 0) continue;
                if (x*x + y*y + z*z > proximityRadius * proximityRadius) continue;
                
                Vec3Int checkPos = { pos.x + x, pos.y + y, pos.z + z };
                if (IsValid(checkPos)) {
                    int cost = g_costGrid[GetIndex(checkPos)];
                    if (cost > 0) { // 비용이 0보다 큰 지역(장애물, 다른 파이프 경로 등)에 대해
                        float distance = sqrt(x*x + y*y + z*z);
                        proximityCost += cost / (distance * distance); // 거리가 멀어질수록 비용은 급격히 감소
                    }
                }
            }
        }
    }
    return proximityCost;
}
DLLEXPORT void PrecomputeDistanceTransform() {
    if (g_costGrid.empty()) return;
    size_t totalSize = (size_t)g_countX * g_countY * g_countZ;
    g_distanceToObstacleGrid.assign(totalSize, MAX_DISTANCE);

    std::queue<Vec3Int> q;

    // 1. 모든 장애물 셀을 큐에 넣고 거리를 0으로 설정
    for (int z = 0; z < g_countZ; ++z) {
        for (int y = 0; y < g_countY; ++y) {
            for (int x = 0; x < g_countX; ++x) {
                Vec3Int pos = { x, y, z };
                if (g_costGrid[GetIndex(pos)] >= OBSTACLE_THRESHOLD) {
                    q.push(pos);
                    g_distanceToObstacleGrid[GetIndex(pos)] = 0;
                }
            }
        }
    }

    // 2. BFS를 수행하여 거리 전파
    while (!q.empty()) {
        Vec3Int current = q.front();
        q.pop();
        int currentDist = g_distanceToObstacleGrid[GetIndex(current)];

        for (const auto& dir : DIRECTIONS) {
            Vec3Int neighbor = { current.x + dir.x, current.y + dir.y, current.z + dir.z };
            if (IsValid(neighbor) && g_distanceToObstacleGrid[GetIndex(neighbor)] == MAX_DISTANCE) {
                g_distanceToObstacleGrid[GetIndex(neighbor)] = currentDist + 1;
                q.push(neighbor);
            }
        }
    }
}

// --- DLL Export 함수 ---
DLLEXPORT void InitializeGrid(int* initialCosts, int countX, int countY, int countZ, Vec3 minBounds, float gridSize) {
    g_countX = countX; g_countY = countY; g_countZ = countZ;
    g_minBounds = minBounds; g_gridSize = gridSize;
    g_costGrid.assign(initialCosts, initialCosts + (size_t)countX * countY * countZ);
}

DLLEXPORT void UpdateCosts(Vec3Int* cellsToUpdate, int count, int costToAdd) {
    if (g_costGrid.empty()) return;
    for (int i = 0; i < count; ++i) {
        if (IsValid(cellsToUpdate[i])) {
            g_costGrid[GetIndex(cellsToUpdate[i])] += costToAdd;
        }
    }
}

DLLEXPORT void ReleaseGrid() { g_costGrid.clear(); g_costGrid.shrink_to_fit(); }

DLLEXPORT int FindPath(Vec3 startPos, Vec3 endPos, Vec3* outPath, int maxPathSize,
                       float w_path, float w_bend, float w_energy, float w_proximity,
                       int pipeRadius, int clearance, int minBendDistance) {
    if (g_costGrid.empty()) return 0;
    float targetDistance = static_cast<float>(pipeRadius + clearance);

    Vec3Int startNodePos = WorldToGrid(startPos);
    Vec3Int endNodePos = WorldToGrid(endPos);

    if (!IsValid(startNodePos) || !IsValid(endNodePos)) return 0;

    std::priority_queue<Node, std::vector<Node>, std::greater<Node>> openSet;
    std::unordered_map<Vec3Int, Node*> allNodes;

    Node* startNode = new Node{ startNodePos, 0, Heuristic(startNodePos, endNodePos), nullptr, -1, 999 };
    openSet.push(*startNode);
    allNodes[startNodePos] = startNode;

    while (!openSet.empty()) {
        Node current = openSet.top();
        openSet.pop();

        Node* currentNode = allNodes[current.pos];
        if (currentNode->gCost < current.gCost) continue;

        if (current.pos == endNodePos) {
            std::vector<Vec3> path;
            Node* curr = currentNode;
            while (curr != nullptr) {
                path.push_back(GridToWorld(curr->pos));
                curr = curr->parent;
            }
            std::reverse(path.begin(), path.end());
            int pathSize = std::min((int)path.size(), maxPathSize);
            for (int i = 0; i < pathSize; ++i) outPath[i] = path[i];
            for (auto const& [key, val] : allNodes) delete val;
            return pathSize;
        }

        for (int i = 0; i < 6; ++i) {
            Vec3Int neighborPos = { currentNode->pos.x + DIRECTIONS[i].x, currentNode->pos.y + DIRECTIONS[i].y, currentNode->pos.z + DIRECTIONS[i].z };
            if (!IsValid(neighborPos)) continue;
            

            // 2. 비용 계산
            float moveCost = w_path;
            if (DIRECTIONS[i].z != 0) moveCost += w_energy;
            if (!IsSpaceAvailable(neighborPos, pipeRadius, clearance)) moveCost+= 10000;

            int stepsSinceBend = currentNode->stepsSinceBend + 1;
            if (currentNode->parent != nullptr && i != currentNode->direction_idx) {
                if(currentNode->stepsSinceBend < minBendDistance) moveCost += w_bend * 10.0f;
                else moveCost += w_bend;
                stepsSinceBend = 1;
            }
            
            // 3. (Soft Constraint) 근접 가중치 추가
            int distToObstacle = g_distanceToObstacleGrid[GetIndex(neighborPos)];
            float distanceCost = std::abs(distToObstacle - targetDistance);
            moveCost += distanceCost * w_proximity;


            float newGCost = currentNode->gCost + moveCost + g_costGrid[GetIndex(neighborPos)];
            
            if (allNodes.find(neighborPos) == allNodes.end() || newGCost < allNodes[neighborPos]->gCost) {
                Node* neighborNode = (allNodes.find(neighborPos) == allNodes.end()) ? new Node() : allNodes[neighborPos];
                neighborNode->pos = neighborPos;
                neighborNode->gCost = newGCost;
                neighborNode->fCost = newGCost + Heuristic(neighborPos, endNodePos);
                neighborNode->parent = currentNode;
                neighborNode->direction_idx = i;
                neighborNode->stepsSinceBend = stepsSinceBend;

                openSet.push(*neighborNode);
                allNodes[neighborPos] = neighborNode;
            }
        }
    }

    for (auto const& [key, val] : allNodes) delete val;
    return 0;
}