import json,glob,os,collections,statistics as st
CUT="2026-09-08T20:34:00"  # round-1 deploy (implementer + reviewer fresh)
root=os.path.expanduser("~/.claude/supervision"); proj=os.path.expanduser("~/.claude/projects/-home-orch-repos-Fincanva")
# 1. member tokens since cut (turns.jsonl) + api_error_status of the 20:34 errors
mem=0; mem_before=0; errs=collections.Counter()
for f in glob.glob(root+"/fincanva-*/*/turns.jsonl"):
    for raw in open(f,"rb"):
        try:o=json.loads(raw)
        except:continue
        if o.get("aiorch_kind")!="turn":continue
        u=o.get("usage") or {}; t=u.get("cache_read_input_tokens",0)+u.get("cache_creation_input_tokens",0)+u.get("input_tokens",0)+u.get("output_tokens",0)
        at=o.get("aiorch_at") or ""
        if at>=CUT:
            mem+=t
            if o.get("is_error"): errs[str(o.get("api_error_status"))+" | "+str((o.get("result") or "")[:80])]+=1
        else: mem_before+=t
print("member tokens (turns.jsonl) before cut %dM, since cut %dM"%(mem_before/1e6,mem/1e6))
print("errors since cut:",dict(errs))
# 2. supervisor tokens since cut from transcripts (dedupe requestId), + all main sessions since cut for cross-check
def sess_tokens(path,since):
    seen={}
    for raw in open(path,"rb"):
        try:o=json.loads(raw)
        except:continue
        m=o.get("message"); u=m.get("usage") if isinstance(m,dict) else None
        if not u: continue
        ts=(o.get("timestamp") or "")
        if ts<since: continue
        rid=o.get("requestId") or o.get("uuid")
        c=u.get("input_tokens",0)+u.get("cache_read_input_tokens",0)+u.get("cache_creation_input_tokens",0)+u.get("output_tokens",0)
        seen[rid]=c
    return sum(seen.values()),len(seen)
sup=0; supcalls=0
for sid in ["fee5320d-78c0-473c-9a47-d575e1ae020f","1ea4486c-ae6a-4a74-9c17-3fc5b0d41cfe"]:
    p="%s/%s.jsonl"%(proj,sid)
    if os.path.exists(p):
        t,n=sess_tokens(p,CUT); sup+=t; supcalls+=n; print("supervisor",sid[:8],"since cut: %dM in %d calls"%(t/1e6,n))
# any other supervisor session id started after cut? (stream process may have been restarted)
allmain=0; alln=0; newsess=0
for p in glob.glob(proj+"/*.jsonl"):
    t,n=sess_tokens(p,CUT); allmain+=t; alln+=n
    if n: newsess+=1
sub=0
for p in glob.glob(proj+"/*/subagents/*.jsonl"):
    t,n=sess_tokens(p,CUT); sub+=t
print("ALL main sessions since cut: %dM in %d calls across %d sessions; subagents since cut %dM"%(allmain/1e6,alln,newsess,sub/1e6))
print("=> total since cut (main+sub) %dM ; members(turns.jsonl) %dM + supervisors %dM"%((allmain+sub)/1e6,mem/1e6,sup/1e6))
# 3. production vs orientation in fresh sessions since cut vs baseline sessions (by tool names + git commit in Bash input)
def mix(paths,since):
    c=collections.Counter(); commits=0
    for p in paths:
        for raw in open(p,"rb"):
            try:o=json.loads(raw)
            except:continue
            if o.get("type")!="assistant": continue
            if (o.get("timestamp") or "")<since and since: continue
            m=o.get("message")
            if not isinstance(m,dict) or not isinstance(m.get("content"),list): continue
            for b in m["content"]:
                if isinstance(b,dict) and b.get("type")=="tool_use":
                    c[b.get("name")]+=1
                    if b.get("name")=="Bash" and "git commit" in json.dumps(b.get("input") or {}): commits+=1
    return c,commits
paths=glob.glob(proj+"/*.jsonl")
c1,k1=mix(paths,CUT); c0,k0=mix(paths,"")
c0=c0-c1; k0=k0-k1
def line(c,k,label,items):
    prod=c["Edit"]+c["Write"]+c["NotebookEdit"]; ori=c["Read"]+c["Grep"]+c["Glob"]+c["Bash"]-k
    print("%-22s tool calls %5d | production (Edit/Write) %4d + commits %3d | orientation (Read/Grep/Bash-noncommit) %5d | Agent %3d | per closed line: prod %.1f ori %.1f"%(label,sum(c.values()),prod,k,ori,c["Agent"],(prod+k)/items,ori/items))
line(c0,k0,"BASELINE (before cut)",23)   # fincanva-1..4 lines closed before: 5+22+1+0=28? use fincanva-2+3 = 23
line(c1,k1,"ROUND1 (since cut)",7)
