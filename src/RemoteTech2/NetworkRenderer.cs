﻿using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace RemoteTech
{
    [Flags]
    public enum MapFilter
    {
        None = 0,
        Omni = 1,
        Dish = 2,
        OmniDish = MapFilter.Omni | MapFilter.Dish,
        Planet = 4,
        Any = 8,
        Path = 16
    }

    public class NetworkRenderer : MonoBehaviour, IConfigNode
    {
        public MapFilter Filter { get; set; }

        private static Texture2D mTexMark;
        private HashSet<BidirectionalEdge<ISatellite>> mEdges = new HashSet<BidirectionalEdge<ISatellite>>();
        private List<NetworkLine> mLines = new List<NetworkLine>();
        private List<NetworkCone> mCones = new List<NetworkCone>();

        public bool ShowOmni { get { return (Filter & (MapFilter.Any | MapFilter.Omni)) == (MapFilter.Any | MapFilter.Omni); } }
        public bool ShowDish { get { return (Filter & (MapFilter.Any | MapFilter.Dish)) == (MapFilter.Any | MapFilter.Dish); } }
        public bool ShowPath { get { return (Filter & MapFilter.Path) == MapFilter.Path || ShowAll; } }
        public bool ShowAll { get { return (Filter & MapFilter.Any) == MapFilter.Any; } }

        static NetworkRenderer()
        {
            RTUtil.LoadImage(out mTexMark, "mark.png");
        }

        public static NetworkRenderer AttachToMapView()
        {
            var renderer = MapView.MapCamera.gameObject.GetComponent<NetworkRenderer>();
            if (renderer)
            {
                Destroy(renderer);
            }

            renderer = MapView.MapCamera.gameObject.AddComponent<NetworkRenderer>();
            renderer.Filter = MapFilter.Any | MapFilter.OmniDish;
            RTCore.Instance.Network.OnLinkAdd += renderer.OnLinkAdd;
            RTCore.Instance.Network.OnLinkRemove += renderer.OnLinkRemove;
            RTCore.Instance.Satellites.OnUnregister += renderer.OnSatelliteUnregister;
            return renderer;
        }

        public void Load(ConfigNode node)
        {
            try
            {
                if (!node.HasValue("MapFilter")) throw new ArgumentException("MapFilter non-exist");
                Filter = (MapFilter)Enum.Parse(typeof(MapFilter), node.GetValue("MapFilter"));
            }
            catch (ArgumentException)
            {
                Filter = MapFilter.Any | MapFilter.OmniDish;
            }
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("MapFilter", Filter.ToString());
        }

        public void OnPreRender()
        {
            if (MapView.MapIsEnabled)
            {
                UpdateNetworkEdges();
                UpdateNetworkCones();
            }
        }

        public void OnGUI()
        {
            if (Event.current.type == EventType.Repaint && MapView.MapIsEnabled)
            {
                foreach (ISatellite s in RTCore.Instance.Satellites.FindCommandStations().Concat(new[] { RTCore.Instance.Network.MissionControl }))
                {
                    var world_pos = ScaledSpace.LocalToScaledSpace(s.Position);
                    if (MapView.MapCamera.transform.InverseTransformPoint(world_pos).z < 0f) continue;
                    Vector3 pos = MapView.MapCamera.camera.WorldToScreenPoint(world_pos);
                    Rect screenRect = new Rect((pos.x - 8), (Screen.height - pos.y) - 8, 16, 16);
                    Graphics.DrawTexture(screenRect, mTexMark, 0, 0, 0, 0);
                }
            }
        }

        private void UpdateNetworkCones()
        {
            var antennas = RTCore.Instance.Antennas.Where(a => a.Powered && a.CanTarget && RTCore.Instance.Network.Planets.ContainsKey(a.Target)).ToList();
            int oldLength = mCones.Count;
            int newLength = antennas.Count;

            // Free any unused lines
            for (int i = newLength; i < oldLength; i++)
            {
                mCones[i].Destroy();
            }
            mCones.RemoveRange(Math.Min(oldLength, newLength), Math.Max(oldLength - newLength, 0));
            mCones.AddRange(Enumerable.Repeat<NetworkCone>(null, Math.Max(newLength - oldLength, 0)));

            for (int i = 0; i < newLength; i++)
            {
                mCones[i] = mCones[i] ?? NetworkCone.Instantiate();
                mCones[i].Material = MapView.fetch.orbitLinesMaterial;
                mCones[i].LineWidth = 2.0f;
                mCones[i].Antenna = antennas[i];
                mCones[i].Planet = RTCore.Instance.Network.Planets[antennas[i].Target];
                mCones[i].Color = Color.gray;
                mCones[i].Active = true;
            }
        }

        private void UpdateNetworkEdges()
        {
            int oldLength = mLines.Count;
            int newLength = mEdges.Count;

            // Free any unused lines
            for (int i = newLength; i < oldLength; i++)
            {
                mLines[i].Destroy();
            }
            mLines.RemoveRange(Math.Min(oldLength, newLength), Math.Max(oldLength - newLength, 0));
            mLines.AddRange(Enumerable.Repeat<NetworkLine>(null, Math.Max(newLength - oldLength, 0)));

            // Iterate over all satellites, updating or creating new lines.
            var it = mEdges.GetEnumerator();
            for (int i = 0; i < newLength; i++)
            {
                it.MoveNext();
                mLines[i] = mLines[i] ?? NetworkLine.Instantiate();
                mLines[i].Material = MapView.fetch.orbitLinesMaterial;
                mLines[i].LineWidth = 5.0f;
                mLines[i].Edge = it.Current;
                mLines[i].Color = CheckColor(it.Current);
                mLines[i].Active = CheckVisibility(it.Current);
            }
        }

        private bool CheckVisibility(BidirectionalEdge<ISatellite> edge)
        {
            var vessel = PlanetariumCamera.fetch.target.vessel;
            var satellite = RTCore.Instance.Satellites[vessel];
            if (satellite != null && ShowPath)
            {
                var connections = RTCore.Instance.Network[satellite];
                if (connections.Any() && connections[0].Contains(edge))
                    return true;
            }
            if (edge.Type == LinkType.Omni && !ShowOmni)
                return false;
            if (edge.Type == LinkType.Dish && !ShowDish)
                return false;
            if (!edge.A.Visible || !edge.B.Visible)
                return false;
            return true;
        }

        private Color CheckColor(BidirectionalEdge<ISatellite> edge)
        {
            var vessel = PlanetariumCamera.fetch.target.vessel;
            var satellite = RTCore.Instance.Satellites[vessel];
            if (satellite != null && ShowPath)
            {
                var connections = RTCore.Instance.Network[satellite];
                if (connections.Any() && connections[0].Contains(edge))
                    return XKCDColors.ElectricLime;
            }
            if (edge.Type == LinkType.Omni)
                return XKCDColors.BrownGrey;
            if (edge.Type == LinkType.Dish)
                return XKCDColors.Amber;

            return XKCDColors.Grey;
        }

        private void OnSatelliteUnregister(ISatellite s)
        {
            mEdges.RemoveWhere(e => e.A == s || e.B == s);
        }

        private void OnLinkAdd(ISatellite a, NetworkLink<ISatellite> link)
        {
            // RTUtil.Log("Link: {0}", mEdges);
            mEdges.Add(new BidirectionalEdge<ISatellite>(a, link.Target, link.Port));
        }

        private void OnLinkRemove(ISatellite a, NetworkLink<ISatellite> link)
        {
            mEdges.Remove(new BidirectionalEdge<ISatellite>(a, link.Target, link.Port));
        }

        public void Detach()
        {
            for (int i = 0; i < mLines.Count; i++)
            {
                mLines[i].Destroy();
            }
            DestroyImmediate(this);
        }

        public void OnDestroy()
        {
            RTCore.Instance.Network.OnLinkAdd -= OnLinkAdd;
            RTCore.Instance.Network.OnLinkRemove -= OnLinkRemove;
            RTCore.Instance.Satellites.OnUnregister -= OnSatelliteUnregister;
        }
    }
}