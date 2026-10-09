using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Relais
{
    /// <summary>
    /// Télécommande : petite page web servie sur le réseau local (Wi-Fi de la maison).
    /// Le téléphone affiche la team ; un appui = une bascule de fenêtre sur le PC (rien n'est envoyé au jeu).
    /// Protégée par un code à 6 chiffres.
    /// </summary>
    public sealed class PhoneServer
    {
        readonly App app;
        TcpListener listener;
        Thread thread;
        volatile bool running;
        int failures;
        DateTime lockedUntil = DateTime.MinValue;
        public string Error { get; private set; }

        public PhoneServer(App app) { this.app = app; }

        public bool Running { get { return running; } }

        public void Start()
        {
            Stop();
            Error = null;
            try
            {
                listener = new TcpListener(IPAddress.Any, app.S.PhonePort);
                listener.Start();
            }
            catch (Exception ex)
            {
                Error = L.T("Port ") + app.S.PhonePort + L.T(" indisponible (") + ex.Message + ")";
                Program.Log("Télécommande : " + Error);
                listener = null;
                return;
            }
            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "Relais télécommande";
            thread.Start();
            Program.Log("Télécommande démarrée : " + Url);
        }

        public void Stop()
        {
            running = false;
            try { if (listener != null) listener.Stop(); } catch { }
            listener = null;
        }

        /// <summary>Adresse à ouvrir sur le téléphone (avec le code).</summary>
        public string Url { get { return "http://" + LocalIp() + ":" + app.S.PhonePort + "/?pin=" + app.S.PhonePin; } }

        public static string LocalIp()
        {
            string best = null;
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    if (desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("hyper-v") || desc.Contains("vpn") || desc.Contains("tap")) continue;
                    foreach (UnicastIPAddressInformation a in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string s = a.Address.ToString();
                        if (s.StartsWith("169.254.") || s.StartsWith("127.")) continue;
                        bool priv = s.StartsWith("192.168.") || s.StartsWith("10.") || (s.StartsWith("172.") && IsPriv172(s));
                        if (priv && ni.GetIPProperties().GatewayAddresses.Count > 0) return s;
                        if (best == null) best = s;
                    }
                }
            }
            catch { }
            return best ?? "127.0.0.1";
        }

        static bool IsPriv172(string s)
        {
            string[] p = s.Split('.');
            int b;
            return p.Length > 1 && int.TryParse(p[1], out b) && b >= 16 && b <= 31;
        }

        void Loop()
        {
            while (running)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch { if (!running) return; Thread.Sleep(200); continue; }
                ThreadPool.QueueUserWorkItem(delegate { try { Serve(c); } catch { } finally { try { c.Close(); } catch { } } });
            }
        }

        // ---------------- HTTP minimal ----------------

        void Serve(TcpClient c)
        {
            c.ReceiveTimeout = 5000; c.SendTimeout = 5000;
            NetworkStream ns = c.GetStream();
            StringBuilder head = new StringBuilder();
            byte[] buf = new byte[1];
            while (head.Length < 8192)
            {
                int r = ns.Read(buf, 0, 1);
                if (r <= 0) break;
                head.Append((char)buf[0]);
                if (head.Length >= 4 && head.ToString(head.Length - 4, 4) == "\r\n\r\n") break;
            }
            string[] lines = head.ToString().Split(new string[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length < 2) return;
            string target = first[1];
            string path = target, query = "";
            int qi = target.IndexOf('?');
            if (qi >= 0) { path = target.Substring(0, qi); query = target.Substring(qi + 1); }
            Dictionary<string, string> q = ParseQuery(query);

            if (path == "/" || path == "/index.html") { Send(ns, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(L.En ? PageEn() : Page())); return; }
            if (path == "/icon.png") { Send(ns, 200, "image/png", IconPng()); return; }
            if (!path.StartsWith("/api/")) { Send(ns, 404, "text/plain", Encoding.UTF8.GetBytes("404")); return; }

            // code obligatoire pour l'API
            string pin;
            q.TryGetValue("pin", out pin);
            if (DateTime.Now < lockedUntil) { Json(ns, 429, "{\"ok\":false,\"error\":\"Trop d'essais, patiente une minute\"}"); return; }
            if (pin != app.S.PhonePin)
            {
                if (++failures >= 8) { failures = 0; lockedUntil = DateTime.Now.AddMinutes(1); }
                Json(ns, 403, "{\"ok\":false,\"error\":\"Code incorrect\"}");
                return;
            }
            failures = 0;

            string result = "{\"ok\":true}";
            if (path == "/api/state") result = OnUi(StateJson);
            else if (path == "/api/go")
            {
                string n; q.TryGetValue("n", out n);
                if (!string.IsNullOrEmpty(n)) app.Ui.Post(delegate { app.Activate(n); }, null);
            }
            else if (path == "/api/do")
            {
                string a; q.TryGetValue("a", out a);
                app.Ui.Post(delegate { Do(a); }, null);
            }
            Json(ns, 200, result);
        }

        void Do(string a)
        {
            switch (a)
            {
                case "next": app.Step(+1); break;
                case "prev": app.Step(-1); break;
                case "leader": app.Leader(); break;
                case "last": app.LastChar(); break;
                case "stack": app.StackWindows(); break;
                case "mosaic": app.MosaicWindows(); break;
                case "pause": app.TogglePause(); break;
                case "invite": app.InviteNext(); break;
                case "endfight": app.Combat.EndNow(); break;
            }
        }

        string OnUi(Func<string> f)
        {
            string r = null;
            try { app.Ui.Send(delegate { r = f(); }, null); } catch { }
            return r ?? "{\"ok\":false}";
        }

        string StateJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"paused\":").Append(app.Paused ? "true" : "false");
            sb.Append(",\"profile\":").Append(Q(app.S.GetCurrent().Name));
            sb.Append(",\"combat\":");
            if (app.Combat.Active) sb.Append("{\"round\":").Append(app.Combat.Round).Append(",\"time\":").Append(Q(app.Combat.Short())).Append(",\"who\":").Append(Q(app.Combat.Current ?? "")).Append("}");
            else sb.Append("null");
            sb.Append(",\"chars\":[");
            List<GameWindow> rot = app.Rotation();
            IntPtr act = app.LastActive;
            for (int i = 0; i < rot.Count; i++)
            {
                GameWindow w = rot[i];
                Color col = Theme.ForCharacter(w.Name, w.Class);
                if (i > 0) sb.Append(',');
                sb.Append("{\"n\":").Append(Q(w.Name)).Append(",\"c\":").Append(Q(w.Class))
                  .Append(",\"col\":\"#").Append(col.R.ToString("x2")).Append(col.G.ToString("x2")).Append(col.B.ToString("x2")).Append('"')
                  .Append(",\"act\":").Append(w.Handle == act ? "true" : "false")
                  .Append(",\"fl\":").Append(app.IsFlashing(w.Handle) ? "true" : "false").Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string Q(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char ch in s ?? "")
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < 32) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                else sb.Append(ch);
            }
            return sb.Append('"').ToString();
        }

        static Dictionary<string, string> ParseQuery(string q)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (string part in q.Split('&'))
            {
                if (part.Length == 0) continue;
                int e = part.IndexOf('=');
                string k = e < 0 ? part : part.Substring(0, e), v = e < 0 ? "" : part.Substring(e + 1);
                try { d[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' ')); } catch { }
            }
            return d;
        }

        static void Json(Stream s, int code, string body) { Send(s, code, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(body)); }

        static void Send(Stream s, int code, string type, byte[] body)
        {
            string status = code == 200 ? "OK" : code == 403 ? "Forbidden" : code == 404 ? "Not Found" : "Error";
            string h = "HTTP/1.1 " + code + " " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
                       "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(h);
            s.Write(hb, 0, hb.Length);
            s.Write(body, 0, body.Length);
            s.Flush();
        }

        byte[] iconCache;
        byte[] IconPng()
        {
            if (iconCache != null) return iconCache;
            try
            {
                byte[] r = null;
                app.Ui.Send(delegate
                {
                    using (Bitmap b = new Bitmap(192, 192))
                    using (Graphics g = Graphics.FromImage(b))
                    using (MemoryStream ms = new MemoryStream())
                    {
                        Theme.Hq(g);
                        g.Clear(Color.FromArgb(28, 22, 17));
                        Brand.DrawShield(g, new RectangleF(30, 26, 132, 140));
                        b.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        r = ms.ToArray();
                    }
                }, null);
                iconCache = r;
            }
            catch { }
            return iconCache ?? new byte[0];
        }

        // ---------------- page ----------------

        static string PageEn()
        {
            string p = Page().Replace("lang='fr'", "lang='en'");
            string[,] r = {
                { "Relais — télécommande", "Relais — remote" }, { "Connexion…", "Connecting…" }, { ">Terminer<", ">End<" },
                { "◀ Préc.", "◀ Prev." }, { ">Chef<", ">Leader<" }, { "Suiv. ▶", "Next ▶" }, { "⇄ D'avant", "⇄ Last" },
                { ">Superposer<", ">Stack<" }, { ">Mosaïque<", ">Tile<" }, { "'Reprendre':'Pause'", "'Resume':'Pause'" },
                { "Code affiché dans Relais (Options &gt; Télécommande)", "Code shown in Relais (Options &gt; Remote)" }, { ">Valider<", ">OK<" },
                { "Un appui = une bascule de fenêtre sur le PC. Relais ne joue jamais à ta place.", "One tap = one window switch on the PC. Relais never plays for you." },
                { "' perso(s) · profil « '+s.profile+' »'+(s.paused?' · EN PAUSE':'')", "' character(s) · profile « '+s.profile+' »'+(s.paused?' · PAUSED':'')" },
                { "'Combat · '", "'Fight · '" }, { "'À toi !'", "'Your turn!'" }, { "'Au premier plan'", "'In front'" },
                { "Aucun perso connecté.", "No character connected." }, { "PC injoignable — même Wi-Fi ? Relais ouvert ?", "PC unreachable — same Wi-Fi? Relais open?" },
                { "'Erreur'", "'Error'" }, { "indexOf('Code')", "indexOf('ode')" } };
            for (int i = 0; i < r.GetLength(0); i++) p = p.Replace(r[i, 0], r[i, 1]);
            return p;
        }

        static string Page()
        {
            return @"<!doctype html><html lang='fr'><head><meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no'>
<meta name='theme-color' content='#1c1611'><meta name='apple-mobile-web-app-capable' content='yes'>
<link rel='icon' href='/icon.png'><link rel='apple-touch-icon' href='/icon.png'>
<title>Relais — télécommande</title>
<style>
:root{--bg:#1c1611;--panel:#2a211a;--panel2:#33291f;--line:#4a3b2c;--text:#f1e6d2;--muted:#b9a88e;--gold:#e0a43a;--green:#7abe68}
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
body{margin:0;background:var(--bg);color:var(--text);font:15px system-ui,-apple-system,Segoe UI,Roboto,sans-serif;padding:16px;padding-bottom:40px}
h1{font:700 20px Georgia,serif;margin:4px 0 2px;display:flex;align-items:center;gap:10px}
h1 img{width:28px;height:28px}
.sub{color:var(--muted);font-size:13px;margin-bottom:14px}
.fight{background:linear-gradient(90deg,rgba(224,164,58,.25),rgba(224,164,58,.08));border:1px solid var(--gold);border-radius:12px;padding:10px 14px;margin-bottom:12px;display:none;justify-content:space-between;align-items:center;color:var(--gold);font-weight:600}
.fight button{background:var(--panel2);color:var(--text);border:1px solid var(--line);border-radius:8px;padding:6px 10px;font:inherit;font-size:13px}
.grid{display:grid;grid-template-columns:1fr 1fr;gap:10px}
.ch{background:linear-gradient(180deg,var(--panel2),var(--panel));border:1px solid var(--line);border-radius:14px;padding:12px;display:flex;gap:10px;align-items:center;min-height:74px;user-select:none}
.ch:active{transform:scale(.97)}
.ch.act{border-color:var(--gold);box-shadow:0 0 0 1px var(--gold) inset}
.ch.fl{animation:pulse 1s infinite}
@keyframes pulse{50%{background:rgba(224,164,58,.28)}}
.av{width:44px;height:44px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-weight:800;color:#1c1611;font-size:18px;flex:none;position:relative}
.num{position:absolute;right:-4px;bottom:-4px;width:18px;height:18px;border-radius:50%;background:var(--gold);font-size:10px;display:flex;align-items:center;justify-content:center;border:2px solid var(--panel)}
.nm{font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.cl{color:var(--muted);font-size:12px}
.fl .cl{color:var(--gold);font-weight:600}
.row{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin-top:14px}
.row button{background:var(--panel2);color:var(--text);border:1px solid var(--line);border-radius:10px;padding:14px 6px;font:inherit;font-weight:600}
.row button:active{background:var(--line)}
.row .gold{background:linear-gradient(180deg,#f0c060,#c98a26);color:#2a1d0c;border-color:#a8701c}
.err{color:#e57b6b;margin:12px 0;display:none}
.pin{display:none;margin-top:30px;text-align:center}
.pin input{font-size:28px;letter-spacing:8px;width:200px;text-align:center;background:var(--panel);color:var(--text);border:1px solid var(--line);border-radius:10px;padding:10px}
.pin button{display:block;margin:14px auto;background:var(--gold);border:0;border-radius:10px;padding:12px 28px;font-weight:700}
.foot{color:var(--muted);font-size:11px;margin-top:22px;text-align:center}
</style></head><body>
<h1><img src='/icon.png' alt=''>Relais</h1>
<div class='sub' id='sub'>Connexion…</div>
<div class='fight' id='fight'><span id='ft'></span><button onclick=""act('endfight')"">Terminer</button></div>
<div class='err' id='err'></div>
<div class='grid' id='grid'></div>
<div class='row' id='acts'>
 <button onclick=""act('prev')"">◀ Préc.</button><button class='gold' onclick=""act('leader')"">Chef</button><button onclick=""act('next')"">Suiv. ▶</button>
 <button onclick=""act('last')"">⇄ D'avant</button><button onclick=""act('stack')"">Superposer</button><button onclick=""act('mosaic')"">Mosaïque</button>
 <button onclick=""act('invite')"">/invite</button><button id='pz' onclick=""act('pause')"">Pause</button><button onclick=""load()"">↻</button>
</div>
<div class='pin' id='pinbox'><div>Code affiché dans Relais (Options &gt; Télécommande)</div><input id='pinin' inputmode='numeric' maxlength='6'><button onclick='savePin()'>Valider</button></div>
<div class='foot'>Un appui = une bascule de fenêtre sur le PC. Relais ne joue jamais à ta place.</div>
<script>
var pin=new URLSearchParams(location.search).get('pin')||localStorage.getItem('relais-pin')||'';
if(pin)localStorage.setItem('relais-pin',pin);
function q(id){return document.getElementById(id)}
function api(p){return fetch('/api/'+p+(p.indexOf('?')<0?'?':'&')+'pin='+encodeURIComponent(pin)).then(function(r){return r.json()})}
function savePin(){pin=q('pinin').value.trim();localStorage.setItem('relais-pin',pin);q('pinbox').style.display='none';load()}
function esc(s){return s.replace(/[&<>""']/g,function(c){return '&#'+c.charCodeAt(0)+';'})}
function go(n){if(navigator.vibrate)navigator.vibrate(15);api('go?n='+encodeURIComponent(n)).then(function(){setTimeout(load,250)})}
function act(a){if(navigator.vibrate)navigator.vibrate(15);api('do?a='+a).then(function(){setTimeout(load,250)})}
function load(){
 if(!pin){q('pinbox').style.display='block';return}
 api('state').then(function(s){
  if(!s.ok){q('err').textContent=s.error||'Erreur';q('err').style.display='block';if(s.error&&s.error.indexOf('Code')>=0){q('pinbox').style.display='block'}return}
  q('err').style.display='none';
  q('sub').textContent=s.chars.length+' perso(s) · profil « '+s.profile+' »'+(s.paused?' · EN PAUSE':'');
  q('pz').textContent=s.paused?'Reprendre':'Pause';
  var f=q('fight');if(s.combat){f.style.display='flex';q('ft').textContent='Combat · '+s.combat.time+(s.combat.who?' · '+s.combat.who:'')}else f.style.display='none';
  var h='';s.chars.forEach(function(c,i){
   h+='<div class=""ch'+(c.act?' act':'')+(c.fl?' fl':'')+'"" data-n=""'+esc(c.n)+'"" onclick=""go(this.dataset.n)"">'+
   '<div class=av style=""background:'+c.col+'"">'+esc(c.n.charAt(0).toUpperCase())+'<span class=num>'+(i+1)+'</span></div>'+
   '<div style=""min-width:0""><div class=nm>'+esc(c.n)+'</div><div class=cl>'+(c.fl?'À toi !':c.act?'Au premier plan':esc(c.c||''))+'</div></div></div>'});
  if(!s.chars.length)h='<div class=sub>Aucun perso connecté.</div>';
  q('grid').innerHTML=h;
 }).catch(function(){q('sub').textContent='PC injoignable — même Wi-Fi ? Relais ouvert ?'});
}
load();setInterval(load,1500);
</script></body></html>";
        }
    }
}
