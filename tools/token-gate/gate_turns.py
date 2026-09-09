import json,glob,os,collections,statistics as st
CUT="2026-09-08T20:34:00Z"  # round-1 deploy (implementer + reviewer fresh)
root=os.path.expanduser("~/.claude/supervision")
def turns():
    for f in glob.glob(root+"/fincanva-*/*/turns.jsonl"):
        orch=f.split("/")[-3]; member=f.split("/")[-2]
        for raw in open(f,"rb"):
            try:o=json.loads(raw)
            except:continue
            if o.get("aiorch_kind")!="turn":continue
            u=o.get("usage") or {}; its=u.get("iterations") or []
            fc=None
            if its: i=its[0]; fc=i.get("input_tokens",0)+i.get("cache_read_input_tokens",0)+i.get("cache_creation_input_tokens",0)
            yield dict(orch=orch,member=member,at=o.get("aiorch_at") or "",sid=o.get("session_id"),calls=o.get("num_turns"),fc=fc,
                tok=u.get("cache_read_input_tokens",0)+u.get("cache_creation_input_tokens",0)+u.get("input_tokens",0)+u.get("output_tokens",0),
                out=u.get("output_tokens",0),new=u.get("cache_creation_input_tokens",0)+u.get("input_tokens",0),dur=o.get("duration_ms") or 0,err=bool(o.get("is_error")))
rows=list(turns())
def role(m): return "imp" if m.startswith("imp") else "rev"
def summ(rs,label):
    if not rs: print(label,"- none"); return
    fc=[r["fc"] for r in rs if r["fc"]]; c=[r["calls"] for r in rs if r["calls"]]
    p90=(sorted(fc)[int(len(fc)*.9)]/1000) if fc else 0
    print("%-36s n=%3d | calls/turn med %4.1f mean %4.1f | ctx1st med %5dk p90 %5dk | tok/turn med %6dk mean %6dk | new/turn med %4dk | out med %3dk | min/turn med %4.1f | err %d"%(
        label,len(rs),st.median(c) if c else 0,st.mean(c) if c else 0,st.median(fc)/1000 if fc else 0,p90,
        st.median([r["tok"] for r in rs])/1000,st.mean([r["tok"] for r in rs])/1000,st.median([r["new"] for r in rs])/1000,st.median([r["out"] for r in rs])/1000,
        st.median([r["dur"] for r in rs])/60000,sum(1 for r in rs if r["err"])))
for rl in ["imp","rev"]:
    summ([r for r in rows if role(r["member"])==rl and r["at"]<CUT],"BASELINE %s (transcript, <20:34Z)"%rl)
    summ([r for r in rows if role(r["member"])==rl and r["at"]>=CUT],"ROUND1   %s (fresh, >=20:34Z)"%rl)
print("\n== fresh turns tonight, one line each")
for r in sorted([r for r in rows if r["at"]>=CUT],key=lambda r:r["at"]):
    print(r["at"][11:19],r["orch"],r["member"],"sid",(r["sid"] or "")[:8],"calls",r["calls"],"ctx1st %dk"%((r["fc"] or 0)/1000),"tok %dk"%(r["tok"]/1000),"new %dk"%(r["new"]/1000),"out %dk"%(r["out"]/1000),"min %.1f"%(r["dur"]/60000),"err",r["err"])
print("\n== tool mix of tonight's fresh sessions (tool names + whether a file-path arg names a channel/archive/PLAN file)")
sids={r["sid"] for r in rows if r["at"]>=CUT and r["sid"]}
proj=os.path.expanduser("~/.claude/projects/-home-orch-repos-Fincanva")
tot=collections.Counter(); chan=0; arch=0; plan=0; sessions=0; firstctx=[]
for sid in sids:
    f="%s/%s.jsonl"%(proj,sid)
    if not os.path.exists(f): continue
    sessions+=1; first=None
    for raw in open(f,"rb"):
        try:o=json.loads(raw)
        except:continue
        m=o.get("message")
        if not isinstance(m,dict):continue
        u=m.get("usage")
        if u and first is None: first=u.get("input_tokens",0)+u.get("cache_read_input_tokens",0)+u.get("cache_creation_input_tokens",0)
        if o.get("type")!="assistant" or not isinstance(m.get("content"),list):continue
        for c in m["content"]:
            if isinstance(c,dict) and c.get("type")=="tool_use":
                tot[c.get("name","?")]+=1
                inp=json.dumps(c.get("input") or {})
                if "channel.md" in inp or "owner-channel" in inp: chan+=1
                if "archive.md" in inp: arch+=1
                if "PLAN.md" in inp: plan+=1
    if first: firstctx.append(first)
print("sessions",sessions,"tools",dict(tot.most_common(8)),"| tool calls naming channel files",chan,"| archive",arch,"| PLAN.md",plan,"| first-call ctx med %dk"%(st.median(firstctx)/1000 if firstctx else 0))
