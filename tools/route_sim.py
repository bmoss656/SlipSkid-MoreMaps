"""Route layout simulator: lays a route out like RoutePlanner, reports overlaps (or crossings with the height gap),
length per surface and the area used.
Usage: python tools/route_sim.py assets/routes/<file>.json <start x,y,z> <heading x,y,z>
(Aqueduc: 195,3,127 -1,0,0; Pizza pizza: -114,37,-199 0,0,1). Keep the shape table in step with RoadKit.cs."""
import math,json,sys
def straight(L,rise=0): return {'A':((-L,5,rise),(-1,0,0)),'B':((0,5,0),(1,0,0))}
def turn(n,rise=0): return {'A':((0,5,0),(1,0,0)),'B':((-(10*n-5),10*n,rise),(0,1,0)),'turn':True,'n':n}
P={'R000':straight(10),'R001':straight(10),'R003':straight(10,2),'R004':straight(10,5),'R009':straight(20,4),'R012':straight(20,5),
   'R013':straight(20,5),'R163':straight(10),'R063':straight(20,9),'R064':straight(20,9),
   'R017':turn(1),'R018':turn(2),'R021':turn(3),'R024':turn(4),'R019':turn(2,4),'R020':turn(2,2),'R022':turn(3,4),
   'R026':turn(4,2),'R052':turn(2,-4),'R053':turn(2,-2),'R067':turn(3),'R068':turn(3),'R069':turn(4)}
def rx(v): x,y,z=v; return (x,z,-y)
def ry(v,t):
    c,s=math.cos(math.radians(t)),math.sin(math.radians(t)); x,y,z=v; return (c*x+s*z,y,-s*x+c*z)
def yaw(v): return math.degrees(math.atan2(v[0],v[2]))
def rot(v,t): return ry(rx(v),t)
def R(v): return tuple(round(c,2)+0.0 for c in v)
SEG={'SandCanyon':{'A':(-20,0,-25),'B':(-20,0,-85),'out':(1,0,0),'box':(-100,-20,-95,-15),'len':40+math.pi*30+40},
     'IceHairpin':{'A':(-40,-4,-5),'B':(-40,-4,-75),'out':(1,0,0),'box':(-90,-40,-80,0),'len':20+math.pi*25+20+20},
     'Chute':{'A':(190,55,-58),'B':(340,30,-58),'outA':(-1,0,0),'out':(1,0,0),'box':(190,340,-63,-53),'len':150}}
FORK={'R078':{'stem':(0,5,0),'L':(-40,25,0),'R':(-40,-15,0)}}
def fork_cells(pos,t,box=(-40,0,-15,25)):
    cells=set()
    for cx in range(box[0]+5,box[1],10):
        for cy in range(box[2]+5,box[3],10):
            c=rot((cx,cy,0),t); cells.add((round(pos[0]+c[0]),round(pos[2]+c[2])))
    return cells
def run(start,head,pieces,out=None):
    cur,h=start,head
    if out is None: out=[]
    for pc in pieces:
        for _ in range(pc.get('count',1)):
            code=pc['type']
            if code=='Cannon':
                cur,h=run(cur,h,[{"type":"R001B101","count":2,"surface":"cannon"}],out); continue
            if 'split' in pc:
                f=FORK[code[:4]]
                t=round((yaw(h)-yaw(rx((-1,0,0))))/90)*90%360
                S=rot(f['stem'],t); pos=R(tuple(cur[i]-S[i] for i in range(3)))
                L=R(tuple(pos[i]+rot(f['L'],t)[i] for i in range(3))); Rr=R(tuple(pos[i]+rot(f['R'],t)[i] for i in range(3)))
                out.append(dict(code=code+' fork',pos=pos,yaw=t,entry=cur,exit=L,head=h,cells=fork_cells(pos,t),surf='asphalt',y=(cur[1],cur[1]),seglen=40))
                a=len(out); lc,lh=run(L,h,pc['split'][0],out); b=len(out); rc,rh=run(Rr,h,pc['split'][1],out)
                la=sum(length(o) for o in out[a:b]); ra=sum(length(o) for o in out[b:])
                m=FORK[pc['merge']['type'][:4]]
                mt=round((yaw(lh)-yaw(rx((1,0,0))))/90)*90%360
                MR=rot(m['R'],mt); mpos=R(tuple(lc[i]-MR[i] for i in range(3)))
                other=R(tuple(mpos[i]+rot(m['L'],mt)[i] for i in range(3)))
                print('SPLIT left path %.0f m, right path %.0f m; right ends %s, merge needs %s, heading match %s' % (la, ra, rc, other, lh==rh))
                if any(abs(other[i]-rc[i])>0.05 for i in range(3)): print('  !! PATHS DO NOT MEET, off by', tuple(round(rc[i]-other[i],2) for i in range(3)))
                stem=R(tuple(mpos[i]+rot(m['stem'],mt)[i] for i in range(3)))
                out.append(dict(code=pc['merge']['type']+' merge',pos=mpos,yaw=mt,entry=lc,exit=stem,head=lh,cells=fork_cells(mpos,mt),surf='asphalt',y=(lc[1],lc[1]),seglen=40))
                cur,h=stem,lh; continue
            if code in SEG:
                sg=SEG[code]; right=pc.get('turn')=='right'
                e,x=(sg['B'],sg['A']) if right else (sg['A'],sg['B'])
                eo=sg.get('outA',sg['out']) if not right else sg['out']
                inward=(-eo[0],0,-eo[2])
                t=round((yaw(h)-yaw(inward))/90)*90%360
                T=lambda v: R(tuple(cur[i]+ry(tuple(v[k]-e[k] for k in range(3)),t)[i] for i in range(3)))
                cells=set()
                x0,x1,z0,z1=sg['box']
                for cx in range(x0+5,x1,10):
                    for cz in range(z0+5,z1,10):
                        c=T((cx,0,cz)); cells.add((round(c[0]),round(c[2])))
                nxt=T(x); nh=R(ry(sg['out'],t))
                out.append(dict(code=code,pos=None,yaw=t,entry=cur,exit=nxt,head=nh,cells=cells,surf=pc.get('surface','canyon'),y=(cur[1],nxt[1]),seglen=sg['len']))
                cur,h=nxt,nh; continue
            if code=='gap':
                nxt=R((cur[0]+h[0]*10,cur[1]-pc.get('drop',0)/pc.get('count',1),cur[2]+h[2]*10))
                out.append(dict(code='gap',entry=cur,exit=nxt,head=h,surf='-')); cur=nxt; continue
            p=P[code[:4]]
            enterB=(pc.get('turn')=='right') if p.get('turn') else (pc.get('slope')=='up')
            e,o=p['B'] if enterB else p['A']; x,ox=p['A'] if enterB else p['B']
            inward=(-o[0],-o[1],-o[2])
            t=round((yaw(h)-yaw(rx(inward)))/90)*90%360
            E=rot(e,t); pos=R(tuple(cur[i]-E[i] for i in range(3)))
            X=rot(x,t); nxt=R(tuple(pos[i]+X[i] for i in range(3))); nh=R(rot(ox,t))
            # footprint cells (10m grid) for overlap check: tile-local box x[-L,0] y[0,W]
            if p.get('turn'): L=W=10*p['n']
            else: L=-p['A'][0][0]; W=10
            cells=set()
            for cx in range(int(L/10)):
                for cy in range(int(W/10)):
                    c=rot((-(cx*10+5),cy*10+5,0),t)
                    cells.add((round(pos[0]+c[0]),round(pos[2]+c[2])))
            lanes=pc.get('lanes',1)
            if lanes>1:
                rgt=(h[2],0,-h[0])  # right of heading (x,z): cross(up, h)
                base=set(cells); cells=set()
                for k in range(lanes):
                    off=(k-(lanes-1)/2)*10
                    for c in base: cells.add((round(c[0]+rgt[0]*off),round(c[1]+rgt[2]*off)))
            out.append(dict(code=code,pos=pos,yaw=t,entry=cur,exit=nxt,head=nh,cells=cells,surf=pc.get('surface','asphalt'),y=(cur[1],nxt[1])))
            cur,h=nxt,nh
    return cur,h
def length(o):
    if o['code']=='gap': return 10
    if 'seglen' in o: return o['seglen']
    if P[o['code'][:4]].get('turn'): return (10*P[o['code'][:4]]['n']-5)*math.pi/2
    return math.dist(o['entry'],o['exit'])
if __name__=='__main__':
    r=json.load(open(sys.argv[1]))
    start=tuple(map(float,sys.argv[2].split(','))); head=tuple(map(float,sys.argv[3].split(',')))
    out=[]; run(start,head,r['pieces'],out)
    seen={}; total=0; bysurf={}
    for i,o in enumerate(out):
        total+=length(o); bysurf[o['surf']]=bysurf.get(o['surf'],0)+length(o)
        for c in o.get('cells',()):
            y=min(o['y']) if o.get('y') else 0
            if c in seen and abs(seen[c][1]-y) < 11: print('OVERLAP', i, o['code'], 'with', seen[c][0], c)
            elif c in seen: print('crossing', i, o['code'], 'over/under', seen[c][0], 'dy', y-seen[c][1])
            seen[c]=(i,y)
        print(i,o['code'],o.get('pos'),o.get('yaw'),'exit',o['exit'],'y',o.get('y'),o['surf'])
    print('length %.0f m'%total, {k:round(v) for k,v in bysurf.items()}, 'min y', min(o['exit'][1] for o in out))
    xs=[c[0] for c in seen]; zs=[c[1] for c in seen]
    print('area x',min(xs)-5,max(xs)+5,'z',min(zs)-5,max(zs)+5)
