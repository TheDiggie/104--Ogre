using System;using System.IO;using System.Linq;using System.Collections.Generic;
using Meridian59.Files;using Meridian59.Files.ROO;using Meridian59.Common.Enums;
// The library's GetVertexData is a port of the game's own d3drender.c and
// is therefore the authority on wall UVs. This renderer computes them with
// a constant it measured off one room. Compare the two.
static class Oracle{ static void Main(string[] a){
 string dir=a.Length>0?a[0]:"/tmp/res";
 var rm=new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
 int walls=0, uOk=0, uBad=0, vOk=0, vBad=0, vTotal=0, vNoTile=0;
 var vBadBy=new Dictionary<string,int>();
 double worstU=0; string worstWhere="";
 var badBy=new Dictionary<string,int>();
 foreach(string p in Directory.GetFiles(dir,"*.roo").OrderBy(x=>x)){
  RooFile roo; try{ roo=new RooFile(p); roo.ResolveResources(rm);}catch{continue;}
  foreach(RooWall w in roo.Walls){
   if(w.RightSide==null) continue;
   ushort num=w.RightSide.MiddleTexture; if(num==0) continue;
   var bgf=rm.GetRoomTexture(num); if(bgf==null||bgf.Frames.Count==0) continue;
   var f=bgf.Frames[0];
   int tw=(int)f.Width, th=(int)f.Height, shrink=(int)(bgf.ShrinkFactor<1?1:bgf.ShrinkFactor);
   RooWall.VertexData vd;
   try{ vd=w.GetVertexData(WallPartType.Middle,false,tw,th,shrink);}catch{continue;}
   walls++;
   // How many times the texture repeats along the wall, library vs here.
   double libSpan = Math.Abs(vd.UV3.X - vd.UV0.X);
   // What this renderer now does: (along/16 + xoffset) * shrink / texH,
   // so the span over the wall is ClientLength * shrink / texH.
   double mineSpan = w.ClientLength * (double)shrink / th;
   // And the vertical scale, which is anchor-independent so it can be
   // compared as a rate: library v per unit of wall height vs ours.
   double dz = vd.P1.Z - vd.P0.Z;
   // WF_NO_VTILE makes GetVertexData move the geometry rather than the
   // UVs (it clips P0.Z and drops P1.Z by 16), so dz there is not the
   // wall's own height and a rate comparison is meaningless. Counted
   // separately; it is the one part of the model still unported.
   if (w.RightSide.Flags.IsNoVTile) vNoTile++;
   else if (Math.Abs(dz) > 0.001){
     // GetVertexData ends by insetting the V range by one texel at each
     // end (UV0.Y += 1/W, UV1.Y -= 1/W). That is an Ogre bleeding guard
     // for filtered quads, not part of Meridian's texture mapping, so it
     // is undone before comparing rates.
     double libRate = ((vd.UV1.Y + 1.0/tw) - (vd.UV0.Y - 1.0/tw)) / dz;
     double mineRate = -(double)shrink / (tw * 16.0);
     vTotal++;
     if (Math.Abs(libRate - mineRate) <= 1e-6*Math.Max(1e-6,Math.Abs(libRate))) vOk++;
     else { vBad++;
       double rr = mineRate/(Math.Abs(libRate)<1e-12?1e-12:libRate);
       string kk=$"x{rr:F2}  ({tw}x{th} shrink{shrink})";
       vBadBy[kk]=vBadBy.TryGetValue(kk,out int cc)?cc+1:1; }
   }
   if(Math.Abs(libSpan-mineSpan) < 1e-4*Math.Max(1,libSpan)) uOk++;
   else{
    uBad++;
    double ratio = mineSpan/Math.Max(1e-9,libSpan);
    string k=$"x{ratio:F2}  ({tw}x{th} shrink{shrink})";
    badBy[k]=badBy.TryGetValue(k,out int c)?c+1:1;
    double err=Math.Abs(libSpan-mineSpan);
    if(err>worstU){worstU=err; worstWhere=$"{Path.GetFileName(p)} wall {w.Num} grd{num} {tw}x{th} s{shrink}: lib {libSpan:F2} repeats, here {mineSpan:F2}";}
   }
  }
 }
 Console.WriteLine($"{walls} wall middles compared against the library's own UVs");
 Console.WriteLine($"  along-wall scale agrees : {uOk}");
 Console.WriteLine($"  disagrees               : {uBad}");
 Console.WriteLine($"  worst: {worstWhere}");
 foreach(var kv in badBy.OrderByDescending(x=>x.Value).Take(6))
  Console.WriteLine($"   {kv.Value,6}  this renderer tiles {kv.Key}");
 Console.WriteLine($"{vTotal} sloped-enough parts compared for vertical scale");
 Console.WriteLine($"  vertical scale agrees   : {vOk}");
 Console.WriteLine($"  disagrees               : {vBad}");
 Console.WriteLine($"  skipped, WF_NO_VTILE    : {vNoTile}");
 foreach(var kv in vBadBy.OrderByDescending(x=>x.Value).Take(6))
  Console.WriteLine($"   {kv.Value,6}  vertical {kv.Key}");

 Sides(dir,rm);
 Flats(dir);
}

// Which end of a wall its texture starts from, per side. GetVertexData
// puts u1 at P1 for the right side and at P2 for the left, so the two
// sides run their texture in opposite directions along the same wall.
// This renderer measures every hit from P1, so the left side has to
// count back. Check the whole texture coordinate, origin included, at
// three points along every wall that has both sides.
static void Sides(string dir, ResourceManager rm){
 int both=0, rOk=0, rBad=0, lOk=0, lBad=0, lWouldFail=0, rBack=0, lBack=0;
 double worst=0; string worstWhere="";
 foreach(string p in Directory.GetFiles(dir,"*.roo").OrderBy(x=>x)){
  RooFile roo; try{ roo=new RooFile(p); roo.ResolveResources(rm);}catch{continue;}
  foreach(RooWall w in roo.Walls){
   if(w.RightSide==null||w.LeftSide==null) continue;
   ushort num=w.RightSide.MiddleTexture; if(num==0) continue;
   ushort lnum=w.LeftSide.MiddleTexture; if(lnum==0) continue;
   var bgf=rm.GetRoomTexture(num); if(bgf==null||bgf.Frames.Count==0) continue;
   var lbgf=rm.GetRoomTexture(lnum); if(lbgf==null||lbgf.Frames.Count==0) continue;
   var f=bgf.Frames[0]; var lf=lbgf.Frames[0];
   int tw=(int)f.Width, th=(int)f.Height, shrink=(int)(bgf.ShrinkFactor<1?1:bgf.ShrinkFactor);
   int ltw=(int)lf.Width, lth=(int)lf.Height, lshrink=(int)(lbgf.ShrinkFactor<1?1:lbgf.ShrinkFactor);
   RooWall.VertexData rv, lv;
   try{ rv=w.GetVertexData(WallPartType.Middle,false,tw,th,shrink);
        lv=w.GetVertexData(WallPartType.Middle,true,ltw,lth,lshrink);}catch{continue;}
   both++;
   double len=w.ClientLength;                 // 1:64, the units xoffset is in
   foreach(double frac in new[]{0.0,0.37,1.0}){
    // The library: u runs from UV0.X at its own P0 to UV3.X at its P3,
    // and P0 is P1 on the right side but P2 on the left.
    double libR = rv.UV0.X + frac*(rv.UV3.X-rv.UV0.X);
    double libL = lv.UV0.X + (1.0-frac)*(lv.UV3.X-lv.UV0.X);
    // This renderer: (along + xoffset) * shrink / texH, along measured
    // from P1 and turned round for the left side.
    // The renderer measures from P1 when exactly one of "left side" and
    // WF_BACKWARDS holds, and adds the offset either way.
    bool rBackF = w.RightSide.Flags.IsBackwards, lBackF = w.LeftSide.Flags.IsBackwards;
    double rAlong = (true ^ rBackF) ? frac*len : (1.0-frac)*len;
    double lAlong = (false ^ lBackF) ? frac*len : (1.0-frac)*len;
    double mineR = (rAlong + w.RightXOffset) * shrink / (double)th;
    double mineL = (lAlong + w.LeftXOffset) * lshrink / (double)lth;
    // What it did before the fix, for scale: always from P1, negated when
    // the wall was backwards.
    double oldL  = ((lBackF ? -frac*len : frac*len) + w.LeftXOffset) * lshrink / (double)lth;
    if(Near(libR,mineR)) rOk++; else { rBad++; if(rBackF) rBack++; Worse(ref worst, ref worstWhere, libR, mineR,
      $"{Path.GetFileName(p)} wall {w.Num} right @{frac}"); }
    if(Near(libL,mineL)) lOk++; else { lBad++; if(lBackF) lBack++; Worse(ref worst, ref worstWhere, libL, mineL,
      $"{Path.GetFileName(p)} wall {w.Num} left @{frac}"); }
    if(!Near(libL,oldL)) lWouldFail++;
   }
  }
 }
 Console.WriteLine($"{both} two-sided wall middles, texture coordinate checked at 3 points each");
 Console.WriteLine($"  right side agrees       : {rOk}   disagrees: {rBad}");
 Console.WriteLine($"  left side agrees        : {lOk}   disagrees: {lBad}");
 Console.WriteLine($"  left side before the fix disagreed: {lWouldFail}");
 if(worst>0) Console.WriteLine($"  worst: {worstWhere} off by {worst:F4} of a texture");
}
static bool Near(double a,double b)=>Math.Abs(a-b)<=1e-4*Math.Max(1.0,Math.Abs(a));
static void Worse(ref double worst, ref string where, double lib, double mine, string what){
 double e=Math.Abs(lib-mine); if(e>worst){worst=e; where=$"{what}: lib {lib:F4}, here {mine:F4}";}}


// RooSubSector.UpdateVertexUV is the authority on floor and ceiling UVs.
// It fills FloorUV per leaf vertex, so every vertex of every leaf is a
// place where this renderer's arithmetic can be checked outright rather
// than argued about. What is checked is the whole rule: the fixed 1/1024
// scale, the swap of X and Y, the sector offsets, and the per-leaf corner
// that FlatAnchors exists to supply.
static void Flats(string dir){
 int leaves=0, verts=0, ok=0, bad=0, anchoredLeaves=0, anchoredVerts=0;
 int lookups=0, lookupOk=0, lookupMiss=0;
 string worst=""; double worstErr=0; string lookupWorst="";

 foreach(string f in Directory.GetFiles(dir,"*.roo").OrderBy(x=>x)){
  RooFile roo; try{ roo=new RooFile(f); }catch{ continue; }
  var anchors=new FlatAnchors(roo);

  foreach(RooSubSector leaf in roo.BSPTreeLeaves){
   if(leaf.Vertices==null||leaf.Vertices.Count<3||leaf.Sector==null) continue;
   // Sloped leaves are built from three points on the plane rather than
   // a corner, which is a different construction and not what
   // FlatAnchors is about.
   if(leaf.Sector.SlopeInfoFloor!=null) continue;

   leaf.UpdateVertexUV(true);
   if(leaf.FloorUV==null||leaf.FloorUV.Length!=leaf.Vertices.Count) continue;
   leaves++;

   // the leaf's own corner, by the library's arithmetic
   float left=0f, top=0f;
   foreach(var v in leaf.Vertices){ if(v.X<left) left=(int)v.X; if(v.Y<top) top=(int)v.Y; }
   bool off0 = left!=0f||top!=0f;
   if(off0) anchoredLeaves++;

   // 1. the rule itself, at every vertex
   float texOffX=leaf.Sector.TextureX*16f, texOffY=leaf.Sector.TextureY*16f;
   for(int i=0;i<leaf.Vertices.Count;i++){
    float vx=leaf.Vertices[i].X, vy=leaf.Vertices[i].Y;
    float u=(vy-top-texOffY)/1024f;
    float v=(vx-left-texOffX)/1024f;

    verts++; if(off0) anchoredVerts++;
    double du=Math.Abs(u-leaf.FloorUV[i].X), dv=Math.Abs(v-leaf.FloorUV[i].Y);
    double err=Math.Max(du,dv);
    if(err<0.001) ok++;
    else{ bad++; if(err>worstErr){ worstErr=err; worst=$"{Path.GetFileName(f)} vertex ({vx},{vy}) mine {u:F3},{v:F3} library {leaf.FloorUV[i].X:F3},{leaf.FloorUV[i].Y:F3}"; } }
   }

   // 2. the lookup, at a point inside the leaf rather than on its edge -
   //    a vertex is exactly where a crossing count is undecided, and the
   //    renderer never asks about one.
   if(!off0) continue;
   double cx=0, cy=0;
   foreach(var v in leaf.Vertices){ cx+=v.X; cy+=v.Y; }
   cx/=leaf.Vertices.Count; cy/=leaf.Vertices.Count;

   lookups++;
   if(anchors.TryAnchor((float)cx,(float)cy,out float ax,out float ay) && ax==left && ay==top) lookupOk++;
   else{ lookupMiss++; if(lookupWorst=="") lookupWorst=$"{Path.GetFileName(f)} leaf centred ({cx:F0},{cy:F0}) wants {left},{top}"; }
  }
 }

 Console.WriteLine($"{verts} flat vertices in {leaves} unsloped leaves compared against the library's own UVs");
 Console.WriteLine($"  agrees                  : {ok}");
 Console.WriteLine($"  disagrees               : {bad}");
 Console.WriteLine($"  on leaves anchored away from the origin: {anchoredVerts} vertices in {anchoredLeaves} leaves");
 if(bad>0) Console.WriteLine($"  worst: {worst} (off by {worstErr:F3})");
 Console.WriteLine($"{lookups} anchored leaves looked up by position, as the renderer does");
 Console.WriteLine($"  found the right corner  : {lookupOk}");
 Console.WriteLine($"  missed                  : {lookupMiss}");
 if(lookupMiss>0) Console.WriteLine($"  first miss: {lookupWorst}");
}
}
