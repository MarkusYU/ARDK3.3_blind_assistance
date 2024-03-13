// Copyright 2022-2024 Niantic.

using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using Random = UnityEngine.Random;
using System.Collections;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;


public class NavMeshModel : MonoBehaviour
  {
    // AudioPlayer parameter
    private float _lastTTSCallTime = 0f;
    private int speedSliderValue = 1;
    private TTSModel model = TTSModel.TTS_1;
    private TTSVoice voice = TTSVoice.Alloy;
    private TTSManager _ttsManager;

    public SpatialTree SpatialTree { get; }

    // Internal container for all surfaces.
    public List<Surface> Surfaces { get; }
    private int _nextSurfaceId = 0;
    private bool _visualise;

    public readonly ModelSettings Settings;

    public NavMeshModel(ModelSettings settings, bool visualise, TTSManager ttsManager)
    {
      Settings = settings;
      _visualise = visualise;
      Surfaces = new List<Surface>();
      SpatialTree = new SpatialTree(Mathf.FloorToInt(settings.SpatialChunkSize / settings.TileSize));
      _ttsManager = ttsManager;
    }

    public void ToggleVisualisation()
    {
      _visualise = !_visualise;
    }

    public HashSet<Vector2Int> Scan(Vector3 origin, Vector3 player_position, float range)
    {
      // Cache parameters
      var kernelSize = Settings.KernelSize;
      var kernelHalfSize = kernelSize / 2;
      var tileSize = Settings.TileSize;
      var tileHalfSize = tileSize / 2.0f;
      const float rayLength = 100.0f;

      float halfRange = range / 2;
      // Debug.Log(range);

      // Calculate bounds for this scan on the grid
      var lowerBoundPosition = new Vector2(origin.x - halfRange, origin.z - halfRange);
      var upperBoundPosition = new Vector2(origin.x + halfRange, origin.z + halfRange);
      var lowerBounds = Utils.PositionToTile(lowerBoundPosition, Settings.TileSize);
      var upperBounds = Utils.PositionToTile(upperBoundPosition, Settings.TileSize);
      if (upperBounds.x - lowerBounds.x < kernelSize ||
        upperBounds.y - lowerBounds.y < kernelSize)
      {
        throw new ArgumentException("Range is too short for the specified tile size.");
      }

      // Calculate tile coordinate of player position and forward position
      var origin_toTile = Utils.PositionToTile(origin, Settings.TileSize);
      var player_position_toTile = Utils.PositionToTile(player_position, Settings.TileSize);

      // Parameters for searching rectangle
      float rectangleWidth = 3.5f;
      float rectangleHeight = 25;

      var pointsInMiddle = GeneratePointsInMiddleOfRectangle(player_position_toTile, origin_toTile, rectangleWidth, rectangleHeight);
      var result = GeneratePointsOnSidesOfRectangle(player_position_toTile, origin_toTile, rectangleWidth, rectangleHeight);
      var pointsOnLeft = result.leftPoints;
      var pointsOnRight = result.rightPoints;

      // Bounds of the search area
      var w = upperBounds.x - lowerBounds.x;
      var h = upperBounds.y - lowerBounds.y;

      // Array to store information on the nodes resulting from this scan
      var scanArea = new GridNode[w * h];

      // Scan heights
      for (var x = 0; x < w; x++)
      {
        for (var y = 0; y < h; y++)
        {
          // Calculate the world position of the ray
          var coords = new Vector2Int(lowerBounds.x + x, lowerBounds.y + y);
          var position = new Vector3
          (
            coords.x * tileSize + tileHalfSize,
            origin.y,
            coords.y * tileSize + tileHalfSize
          );
          // Debug.Log($"position: {position}");

          var arrayIndex = y * w + x;

          if (_visualise)
            Debug.DrawLine(position + Vector3.down, position + 2*Vector3.down, Color.green, 0.5f);

          // Raycast for height
          Vector3 Vector1 = new Vector3(0, -1, 0);
          var elevation = 
            Physics.Raycast
            (
              new Ray(position, Vector3.down),
              out RaycastHit hit,
              rayLength,
              layerMask: Settings.LayerMask
            )
              ? hit.point.y
              : -100;

          scanArea[arrayIndex] = new GridNode(coords)
          {
            DiffFromNeighbour = float.MaxValue, Elevation = elevation
          };
        }
      }

      // This set is used to register nodes that are obviously occupied
      var invalidate = new HashSet<GridNode>();

      // Calculate areal properties
      var kernel = new Vector3[kernelSize * kernelSize];
      for (var x = kernelHalfSize; x < w - kernelHalfSize; x++)
      {
        for (var y = kernelHalfSize; y < h - kernelHalfSize; y++)
        {
          // Construct kernel for this grid cell using its neighbours
          var kernelIndex = 0;
          for (var kx = -kernelHalfSize; kx <= kernelHalfSize; kx++)
          {
            for (var ky = -kernelHalfSize; ky <= kernelHalfSize; ky++)
            {
              var x1 = Mathf.Clamp(kx + x, 0, w - 1);
              var y1 = Mathf.Clamp(ky + y, 0, h - 1);
              kernel[kernelIndex++] = Utils.GridNodeToPosition(scanArea[y1 * w + x1], Settings.TileSize);
            }
          }

          var idx = y * w + x;

          // Try to fit a plane on the neighbouring points
          Utils.FastFitPlane(kernel, out Vector3 _, out Vector3 normal);

          // Assign standard deviation and slope angle
          var slope = Mathf.Abs(90.0f - Vector3.Angle(Vector3.forward, normal));
          var std = Utils.CalculateStandardDeviation(kernel.Select(pos => pos.y));
          scanArea[idx].Deviation = std;

          // Collect nodes that are occupied
          var isWalkable = std < Settings.KernelStdDevTol &&
            slope < Settings.MaxSlope &&
            scanArea[idx].Elevation > Settings.MinElevation;

          if (!isWalkable)
            invalidate.Add(scanArea[idx]);
        }
      }

      // Remove nodes that are occupied from existing planes
      HashSet<Vector2Int> removedNodes = InvalidateNodes(invalidate);

      var open = new Queue<GridNode>();
      var closed = new HashSet<GridNode>();
      var eligible = new HashSet<GridNode>();

      // Define seed as the center of the search area
      open.Enqueue(scanArea[(h / 2) * w + (w / 2)]);
      while (open.Count > 0)
      {
        // Extract current tile
        var currentNode = open.Dequeue();

        // Consider this node to be visited
        closed.Add(currentNode);

        if (invalidate.Contains(currentNode))
          continue; // Skip this node as it is occupied

        // Register this tile as unoccupied...
        eligible.Add(currentNode);

        var neighbours = Utils.GetNeighbours(currentNode.Coordinates);
        foreach (var neighbour in neighbours)
        {

          // Get the coordinates transformed to our local scan area
          var transformedNeighbour = neighbour - lowerBounds;
          if (transformedNeighbour.x < kernelHalfSize ||
            transformedNeighbour.x >= w - kernelHalfSize ||
            transformedNeighbour.y < kernelHalfSize ||
            transformedNeighbour.y >= h - kernelHalfSize)
          {
            continue; // Out of bounds
          }

          var arrayIndex = transformedNeighbour.y * w + transformedNeighbour.x;

          // If we've been here before
          if (closed.Contains(scanArea[arrayIndex]))
            continue;

          var diff = Mathf.Abs(currentNode.Elevation - scanArea[arrayIndex].Elevation);
          if (scanArea[arrayIndex].DiffFromNeighbour > diff)
          {
            scanArea[arrayIndex].DiffFromNeighbour = diff;
          }

          // Can we walk from the current node to this neighbour?
          var isEligible = !open.Contains(scanArea[arrayIndex]) &&
            scanArea[arrayIndex].DiffFromNeighbour <= Settings.StepHeight;

          if (isEligible)
            open.Enqueue(scanArea[arrayIndex]);
        }
      }

      if (eligible.Count >= 2)
      {
        // Merge newly found unoccupied areas with existing planes
        MergeNodes(eligible);
      }

      // Find invalid points on left and right side
      var invalidPointsInMiddle = FindInvalidPoints(invalidate, pointsInMiddle);
      var invalidPointsOnLeft = FindInvalidPoints(invalidate, pointsOnLeft);
      var invalidPointsOnRight = FindInvalidPoints(invalidate, pointsOnRight);

      // If both sides have invalid points
      if (invalidPointsOnLeft.Count > 20 && invalidPointsOnRight.Count > 20)
      {
        var pathResult = ProcessDirectionalChecks(player_position_toTile, origin_toTile, invalidate);
        // Check the result from ProcessDirectionalChecks
        if (pathResult == "none")
        {
            // If the no other path, give prompt "No available path ahead"
            CallTTS("No available path ahead.");
        }
        else
        {
            // If a path is found at a specific direction
            CallTTS($"No available path ahead. Path found at {pathResult}");
        }


      }
      else if ((invalidPointsOnLeft.Count > (invalidPointsOnRight.Count + 1)) && invalidPointsOnLeft.Count > 0)
      {
        var pathResult = ProcessDirectionalChecks(player_position_toTile, origin_toTile, invalidate);
        // If obstacle is found
        if (pathResult == "12 o'clock" || pathResult == "none") {
          CallTTS("Obstacle to your front left");
        }
        // If user is not facing the right direction of path
        // else {
        //     // Check if 'pathResult' does not contain any of the specified strings
        //     var disallowedStrings = new[] { "4", "5", "6", "7", "8" };
        //     if (!disallowedStrings.Any(pathResult.Contains)) {
        //         CallTTS($"Path at your {pathResult}");
        //     }
        // }
        
      }
      // If invalid points found on the right side
      else if (((invalidPointsOnLeft.Count + 1) < invalidPointsOnRight.Count) && invalidPointsOnRight.Count > 3)
      {
        var pathResult = ProcessDirectionalChecks(player_position_toTile, origin_toTile, invalidate);
        // If obstacle is found
        if (pathResult == "12 o'clock" || pathResult == "none") {
          CallTTS("Obstacle to your front right");
        }
        // If user is not facing the right direction of path
        // else {
        //     // Check if 'pathResult' does not contain any of the specified strings
        //     var disallowedStrings = new[] { "4", "5", "6", "7", "8" };
        //     if (!disallowedStrings.Any(pathResult.Contains)) {
        //         CallTTS($"Path at your {pathResult}");
        //     }
        // }
      }
      else if (invalidPointsInMiddle.Count > 5)
      {
         CallTTS("Obstacle right ahead");
      }
      return removedNodes;
    }

    /// Removes all surfaces from the board.
    public void Clear()
    {
      if (Surfaces.Count == 0)
        return ;

      Surfaces.Clear();
      SpatialTree.Clear();
    }

    public void Prune(Vector3 keepNodesOrigin, float range)
    {
      if (Surfaces.Count == 0)
        return;

      float halfRange = range / 2;

      var topRight = keepNodesOrigin +
        Vector3.right * halfRange +
        Vector3.forward * halfRange;

      var bottomLeft = keepNodesOrigin +
        Vector3.left * halfRange +
        Vector3.back * halfRange;

      var min = Utils.PositionToGridNode(bottomLeft, Settings.TileSize);
      var max = Utils.PositionToGridNode(topRight, Settings.TileSize);

      var bounds = new Bounds(min.Coordinates, max.Coordinates.x - min.Coordinates.x);
      var toKeep = SpatialTree.Query(withinBounds: bounds).ToList();

      SpatialTree.Clear();
      SpatialTree.Insert(toKeep);

      // Remove tiles for surfaces
      Surfaces.ForEach(surface => surface.Intersect(toKeep));

      // Clean empty surfaces
      Surfaces.RemoveAll(surface => surface.IsEmpty);
    }

    /// Invalidates the specified nodes of existing planes.
    private HashSet<Vector2Int> InvalidateNodes(HashSet<GridNode> nodes)
    {
      // Remove nodes from registry
      HashSet<Vector2Int> removedNodes = SpatialTree.Remove(nodes);

      // Remove nodes from its respective surfaces
      Surfaces.ForEach(entry => entry.Except(nodes));

      // Clean up empty planes
      Surfaces.RemoveAll(entry =>entry.IsEmpty);

      return removedNodes;
    }

    /// Merges new unoccupied nodes with existing planes. If the nodes cannot be merged, a new plane is created.
    private void MergeNodes(HashSet<GridNode> nodes)
    {
      // Register new unoccupied nodes
      SpatialTree.Insert(nodes);

      // Create a new planes from the provided (unoccupied) nodes
      var candidate = new Surface(nodes, _nextSurfaceId);
      _nextSurfaceId++;

      // Just add the candidate plane to the list if this is the first one we found
      if (Surfaces.Count == 0)
      {
        Surfaces.Add(candidate);
        return;
      }

      // Gather overlapping planes
      var overlappingPlanes = Surfaces.Where(entry => entry.Overlaps(candidate)).ToList();

      // No overlap, add candidate as a new plane
      if (!overlappingPlanes.Any())
      {
        Surfaces.Add(candidate);
        return;
      }

      // Find an overlapping plane that satisfies the merging conditions
      var anchorPlane = overlappingPlanes.FirstOrDefault
      (
        entry =>
          entry.CanMerge(candidate, Settings.StepHeight * 2.0f)
      );

      // No such plane
      if (anchorPlane == null)
      {
        // Exclude its nodes from existing planes
        foreach (var surface in overlappingPlanes)
        {
          surface.Except(candidate);
        }

        // Remove planes that were a subset of the candidate
        Surfaces.RemoveAll(surface => surface.IsEmpty);

        // Add candidate as a new plane
        Surfaces.Add(candidate);
        return;
      }

      // Base plane found to merge the new nodes to
      anchorPlane.Merge(candidate);

      // Iterate through other overlapping planes except this base plane
      overlappingPlanes.Remove(anchorPlane);
      foreach (var entry in overlappingPlanes)
      {
        // Either merge or exclude nodes
        if (anchorPlane.CanMerge(entry, Settings.StepHeight * 2.0f))
        {
          anchorPlane.Merge(entry);
          Surfaces.Remove(entry);
        }
        else
        {
          entry.Except(candidate);
        }
      }
    }

    public bool FindRandomPosition(out Vector3 randomPosition)
    {
      if (Surfaces.Count == 0)
      {
        randomPosition = Vector3.zero;
        return false;
      }

      int randomSurface = Random.Range(0, Surfaces.Count-1);
      int randomNode = Random.Range(0, Surfaces[randomSurface].Elements.Count());

      randomPosition = Utils.GridNodeToPosition
        (Surfaces[randomSurface].Elements.ElementAt(randomNode), Settings.TileSize);

      return true;
    }

    // Finding points coordinates inside the rectangle
    public List<Vector2> GeneratePointsInsideRectangle(Vector2 userPosition, Vector2 forwardPoint, float rectangleWidth, float rectangleHeight)
    {
        List<Vector2> points = new List<Vector2>();

        Vector2 direction = (forwardPoint - userPosition).normalized;
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);

        // Calculate the four corners of the rectangle
        Vector2 corner1 = userPosition + perpendicular * rectangleWidth / 2;
        Vector2 corner2 = userPosition - perpendicular * rectangleWidth / 2;
        Vector2 corner3 = corner1 + direction * rectangleHeight;
        Vector2 corner4 = corner2 + direction * rectangleHeight;
        // Debug.Log($"Corner1: ({corner1.x}, {corner1.y})");
        // Debug.Log($"Corner2: ({corner2.x}, {corner2.y})");
        // Debug.Log($"Corner3: ({corner3.x}, {corner3.y})");
        // Debug.Log($"Corner4: ({corner4.x}, {corner4.y})");

        // Approximate bounding area for checking points inside it
        float minX = Mathf.Min(corner1.x, corner2.x, corner3.x, corner4.x);
        float maxX = Mathf.Max(corner1.x, corner2.x, corner3.x, corner4.x);
        float minY = Mathf.Min(corner1.y, corner2.y, corner3.y, corner4.y);
        float maxY = Mathf.Max(corner1.y, corner2.y, corner3.y, corner4.y);

        for (int x = (int)Mathf.Floor(minX); x <= (int)Mathf.Ceil(maxX); x++)
        {
          for (int y = (int)Mathf.Floor(minY); y <= (int)Mathf.Ceil(maxY); y++)
          {
            Vector2 point = new Vector2(x, y);
            // If the point is inside the searching rectangle
            if (IsPointInRectangle(point, corner1, corner2, corner3, corner4))
            {
                points.Add(point);
                // Debug.Log($"Print Point: ({point.x}, {point.y})");
            }
          }
        }

        return points;
    }

    // Finding points coordinates in the middle of rectangle
    public List<Vector2> GeneratePointsInMiddleOfRectangle(Vector2 userPosition, Vector2 forwardPoint, float rectangleWidth, float rectangleHeight)
    {
        List<Vector2> points = new List<Vector2>();

        Vector2 direction = (forwardPoint - userPosition).normalized;
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);

        float rectangleHalfWidth = rectangleWidth / 2;
        // Calculate the four corners of the rectangle
        Vector2 corner1 = userPosition + perpendicular * rectangleHalfWidth / 2;
        Vector2 corner2 = userPosition - perpendicular * rectangleHalfWidth / 2;
        Vector2 corner3 = corner1 + direction * rectangleHeight;
        Vector2 corner4 = corner2 + direction * rectangleHeight;

        // Approximate bounding area for checking points inside it
        float minX = Mathf.Min(corner1.x, corner2.x, corner3.x, corner4.x);
        float maxX = Mathf.Max(corner1.x, corner2.x, corner3.x, corner4.x);
        float minY = Mathf.Min(corner1.y, corner2.y, corner3.y, corner4.y);
        float maxY = Mathf.Max(corner1.y, corner2.y, corner3.y, corner4.y);

        for (int x = (int)Mathf.Floor(minX); x <= (int)Mathf.Ceil(maxX); x++)
        {
          for (int y = (int)Mathf.Floor(minY); y <= (int)Mathf.Ceil(maxY); y++)
          {
            Vector2 point = new Vector2(x, y);
            // If the point is inside the searching rectangle
            if (IsPointInRectangle(point, corner1, corner2, corner3, corner4))
            {
                points.Add(point);
                // Debug.Log($"Print Point: ({point.x}, {point.y})");
            }
          }
        }

        return points;
    }

    // Finding points coordinates on the left side and right side of the rectangle
    public (List<Vector2> leftPoints, List<Vector2> rightPoints) GeneratePointsOnSidesOfRectangle(Vector2 userPosition, Vector2 forwardPoint, float rectangleWidth, float rectangleHeight)
    {
        List<Vector2> leftPoints = new List<Vector2>();
        List<Vector2> rightPoints = new List<Vector2>();

        // Calculate direction vector from user position to forward point and normalize
        Vector2 direction = (forwardPoint - userPosition).normalized;
        // Calculate perpendicular vector to direction
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);

        // Determine the four corners of the rectangle
        Vector2 corner1 = userPosition + perpendicular * rectangleWidth / 2;
        Vector2 corner2 = userPosition - perpendicular * rectangleWidth / 2;
        Vector2 corner3 = corner1 + direction * rectangleHeight;
        Vector2 corner4 = corner2 + direction * rectangleHeight;

        // Approximate bounding area for checking points inside it
        float minX = Mathf.Min(corner1.x, corner2.x, corner3.x, corner4.x);
        float maxX = Mathf.Max(corner1.x, corner2.x, corner3.x, corner4.x);
        float minY = Mathf.Min(corner1.y, corner2.y, corner3.y, corner4.y);
        float maxY = Mathf.Max(corner1.y, corner2.y, corner3.y, corner4.y);

        for (int x = (int)Mathf.Floor(minX); x <= (int)Mathf.Ceil(maxX); x++)
        {
          for (int y = (int)Mathf.Floor(minY); y <= (int)Mathf.Ceil(maxY); y++)
          {
            Vector2 point = new Vector2(x, y);
            if (IsPointInRectangle(point, corner1, corner2, corner3, corner4))
            {
              // Add the point to left side or right side list
              if (IsPointLeftOfLine(userPosition, forwardPoint, point))
              {
                  leftPoints.Add(point);
              }
              else
              {
                  rightPoints.Add(point);
              }
            }
          }
        }
        return (leftPoints, rightPoints);
    }
    
    // Check if the point is on the left side of the middle line in the rectangle
    private bool IsPointLeftOfLine(Vector2 lineStart, Vector2 lineEnd, Vector2 point)
    {
        return ((lineEnd.x - lineStart.x) * (point.y - lineStart.y) - (lineEnd.y - lineStart.y) * (point.x - lineStart.x)) > 0;
    }

    // Check if the point is in the rectangle area
    public bool IsPointInRectangle(Vector2 point, Vector2 corner1, Vector2 corner2, Vector2 corner4, Vector2 corner3)
    {
        // Calculate the area of the rectangle as two triangles
        float fullArea = TriangleArea(corner1, corner2, corner3) + TriangleArea(corner1, corner3, corner4);
        
        // Calculate the area of triangles formed by the point and corners of the rectangle
        float area1 = TriangleArea(point, corner1, corner2);
        float area2 = TriangleArea(point, corner2, corner3);
        float area3 = TriangleArea(point, corner3, corner4);
        float area4 = TriangleArea(point, corner4, corner1);
        
        // Compare the sum of the triangle areas with the rectangle's area
        float totalArea = area1 + area2 + area3 + area4;
        
        // Allow for a small error margin due to floating-point arithmetic
        const float epsilon = 0.0001f;
        
        // If the total area is equal to the rectangle's area, the point is inside
        return Mathf.Abs(totalArea - fullArea) < epsilon;
    }

    float TriangleArea(Vector2 a, Vector2 b, Vector2 c)
    {
        return Mathf.Abs(a.x * (b.y - c.y) + b.x * (c.y - a.y) + c.x * (a.y - b.y)) / 2.0f;
    }

    // Find invalid points in the list of points passed in
    public static List<Vector2> FindInvalidPoints(HashSet<GridNode> invalidate, List<Vector2> points)
    {
       
        List<Vector2> invalidPoints = new List<Vector2>();
        HashSet<Vector2> invalidCoordinates = new HashSet<Vector2>(invalidate.Select(node => (Vector2)node.Coordinates));

        foreach (var point in points)
        {
            // Check if the current point's coordinates are in the invalidCoordinates set
            if (invalidCoordinates.Contains(point))
            {
                invalidPoints.Add(point);
            }
        }

        // Return the list of coordinates that are also invalidated
        return invalidPoints;
    }

    private void CallTTS(string ttsMessage)
    {
      // If time interval from the start of last call is greater than 4s
      if (Time.time - _lastTTSCallTime >= 5.0f)
      {
          Debug.Log(ttsMessage);
          _ttsManager.SynthesizeAndPlay(ttsMessage, model, voice, speedSliderValue);
          // Update the last TTS call time
          if (ttsMessage.Length > 40) {
            _lastTTSCallTime = Time.time + 3.0f;
          }
          else {
            _lastTTSCallTime = Time.time;
          }
          
      }
      else
      {
          Debug.Log("Waiting to call TTS again...");
      }
    }

    // Generate 12 clock direction points coordinates based on current user position
    public List<Vector2> GenerateCirclePoints(Vector2 playerPosition, Vector2 forwardPosition, float radius)
    {
        List<Vector2> points = new List<Vector2>();

        // Calculate the initial direction from the player to the forward position
        Vector2 initialDirection = (forwardPosition - playerPosition).normalized;

        // Calculate the angle for the initial direction
        float initialAngle = Mathf.Atan2(initialDirection.y, initialDirection.x) * Mathf.Rad2Deg;

        // Generate points at 30-degree intervals, moving clockwise
        for (int i = 0; i < 12; i++)
        {
            float angle = initialAngle - (30f * i); // Subtract to move clockwise
            // Ensure the angle is within the range [0, 360)
            angle = (angle + 360f) % 360f;

            float radian = angle * Mathf.Deg2Rad;
            Vector2 point = new Vector2(
                Mathf.RoundToInt(playerPosition.x + radius * Mathf.Cos(radian)),
                Mathf.RoundToInt(playerPosition.y + radius * Mathf.Sin(radian))
            );

            points.Add(point);
        }

        return points;
    }

    // Check available paths around user position in 12 clock direction
    public string ProcessDirectionalChecks(Vector2 playerPosition, Vector2 forwardPosition, HashSet<GridNode> invalidate)
    {
        float radius = 10f;
        float rectangleWidth = 3f;
        float rectangleHeight = 20f;
        List<Vector2> circlePoints = GenerateCirclePoints(playerPosition, forwardPosition, radius);

        List<int> validAngles = new List<int>();

        // Generate points at 30-degree intervals and collect valid angles
        for (int i = 0; i < circlePoints.Count; i++)
        {
            Vector2 directionPoint = circlePoints[i];
            int angle = i * 30;

            List<Vector2> points = GeneratePointsInsideRectangle(playerPosition, directionPoint, rectangleWidth, rectangleHeight);
            var invalidPoints = FindInvalidPoints(invalidate, points);

            // Collect angles with fewer than 5 invalid points
            if (i == 0) {
              if (invalidPoints.Count < 15)
              {
                  validAngles.Add(angle);
              }
            }
            else 
            {
              if ((invalidPoints.Count < 10))
              {
                validAngles.Add(angle);
              } 
            }
            // Debug.Log($"Angle: {angle} degrees, Invalid Points count: {invalidPoints.Count}");
        }

        // Prioritize angles based on their closeness to 12 o'clock (0/360 degrees)
        int? bestAngle = null;
        foreach (int angle in validAngles)
        {
            if (bestAngle == null)
            {
                bestAngle = angle;
            }
            else
            {
                // Calculate "closeness" of each angle to 0/360 degrees
                int currentAngleDifference = Math.Min(Math.Abs(360 - angle), angle);
                int bestAngleDifference = Math.Min(Math.Abs(360 - bestAngle.Value), bestAngle.Value);

                // Update bestAngle if current angle is closer to 0/360 degrees
                if (currentAngleDifference < bestAngleDifference)
                {
                    bestAngle = angle;
                }
                // If they are equally close, prefer the lower angle
                else if (currentAngleDifference == bestAngleDifference && angle < bestAngle.Value)
                {
                    bestAngle = angle;
                }
            }
        }

        // If no suitable angle was found, return "none"
        if (bestAngle == null)
        {
            return "none";
        }
        else
        {
            // Convert the best angle to clock position and return it
            return AngleToClockPosition(bestAngle.Value);
        }
    }

    private string AngleToClockPosition(int angle)
    {
        int clockPosition = angle / 30; // Convert angle to clock position index
        if(clockPosition == 0) return "12 o'clock";
      
        return $"{clockPosition} o'clock";
    }

}
