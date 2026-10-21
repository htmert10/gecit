using TLab.WebView;
using UnityEngine;
using UnityEngine.UI;

// Havadaki tarayıcı penceresi: açılışta tarayıcıyı başlatır, görüntüyü saniyede ~25 kez günceller.
// Sayfaya küçük bir betik ekler:
//  - Tam ekran: Android'in tam ekran görünümü yerine videoyu pencerenin içinde büyütür (alttaki beyaz alan olmaz).
//  - Klavye: telefonun klavyesini kapatır, yazı alanına dokununca VR klavyeyi açtırır.
public class VRBrowser : MonoBehaviour
{
    public static string Status = "-";

    public BrowserContainer container;
    public RawImage view;
    public string windowName;
    public float updatesPerSecond = 25f;
    float next;
    float nextInject, nextStatus;
    string script;

    const string Script = @"(function(){
if(window.__gecit)return;window.__gecit=1;
var NAME='%NAME%';
function send(m,a){try{window.tlab.unitySendMessage('Klavye',m,a);}catch(e){}}
var st=document.createElement('style');
st.textContent='.gecit-fs{position:fixed!important;left:0!important;top:0!important;right:0!important;bottom:0!important;width:100vw!important;height:100vh!important;max-width:none!important;max-height:none!important;margin:0!important;padding:0!important;transform:none!important;z-index:2147483647!important;background:#000!important}'+
'.gecit-fs video{width:100vw!important;height:100vh!important;left:0!important;top:0!important;object-fit:contain!important}'+
'html.gecit-lock,html.gecit-lock body{overflow:hidden!important}';
(document.head||document.documentElement).appendChild(st);
var cur=null;
function fire(){['fullscreenchange','webkitfullscreenchange'].forEach(function(n){document.dispatchEvent(new Event(n,{bubbles:true}));});window.dispatchEvent(new Event('resize'));}
function cinema(m){try{window.tlab.unitySendMessage('Sinema',m,NAME);}catch(e){}}
function enter(el){if(!el)return Promise.resolve();if(cur&&cur!==el)cur.classList.remove('gecit-fs');cur=el;el.classList.add('gecit-fs');document.documentElement.classList.add('gecit-lock');window.scrollTo(0,0);fire();cinema('Enter');return Promise.resolve();}
function exit(){if(!cur)return Promise.resolve();cur.classList.remove('gecit-fs');cur=null;document.documentElement.classList.remove('gecit-lock');fire();cinema('Exit');return Promise.resolve();}
function def(o,p,g){try{Object.defineProperty(o,p,{get:g,configurable:true});}catch(e){}}
def(document,'fullscreenElement',function(){return cur;});
def(document,'webkitFullscreenElement',function(){return cur;});
def(document,'webkitCurrentFullScreenElement',function(){return cur;});
def(document,'webkitIsFullScreen',function(){return !!cur;});
def(document,'fullscreenEnabled',function(){return true;});
def(document,'webkitFullscreenEnabled',function(){return true;});
Element.prototype.requestFullscreen=function(){return enter(this);};
Element.prototype.webkitRequestFullscreen=function(){return enter(this);};
Element.prototype.webkitRequestFullScreen=function(){return enter(this);};
HTMLVideoElement.prototype.webkitEnterFullscreen=function(){enter(this.closest('.html5-video-player')||this);};
document.exitFullscreen=exit;document.webkitExitFullscreen=exit;document.webkitCancelFullScreen=exit;
try{if(screen.orientation){screen.orientation.lock=function(){return Promise.resolve();};screen.orientation.unlock=function(){};}}catch(e){}
function player(){var v=document.querySelector('video');if(!v)return null;return document.getElementById('player-container-id')||v.closest('.html5-video-player')||v.parentElement||v;}
window.__gecitToggleFs=function(){if(cur){exit();}else{enter(player());}};
window.__gecitExitFs=exit;
function editable(el){if(!el||!el.tagName)return false;var t=el.tagName;if(t==='TEXTAREA')return true;if(t==='INPUT'){var ty=(el.getAttribute('type')||'text').toLowerCase();return ['text','search','email','url','tel','password','number'].indexOf(ty)>=0;}return el.isContentEditable===true;}
function quiet(el){if(editable(el)&&el.getAttribute('inputmode')!=='none'){el.setAttribute('data-gecit-im',el.getAttribute('inputmode')||'');el.setAttribute('inputmode','none');}}
window.__gecitQuiet=function(){var l=document.querySelectorAll('input,textarea,[contenteditable]');for(var i=0;i<l.length;i++)quiet(l[i]);};
['pointerdown','touchstart','mousedown'].forEach(function(n){document.addEventListener(n,function(e){var el=e.target;if(el&&el.closest){var c=el.closest('input,textarea,[contenteditable]');if(c)quiet(c);}},true);});
document.addEventListener('focusin',function(e){if(editable(e.target)){quiet(e.target);send('Show',NAME);}},true);
document.addEventListener('focusout',function(e){if(editable(e.target))send('Hide',NAME);},true);
document.addEventListener('keydown',function(e){if(e.key==='Escape'&&cur)exit();},true);
window.__gecitType=function(s){var el=document.activeElement;if(!editable(el))return;if(!document.execCommand('insertText',false,s)){var a=el.selectionStart||el.value.length,b=el.selectionEnd||a;el.value=el.value.slice(0,a)+s+el.value.slice(b);el.selectionStart=el.selectionEnd=a+s.length;el.dispatchEvent(new Event('input',{bubbles:true}));}};
window.__gecitBlur=function(){var el=document.activeElement;if(el&&el.blur)el.blur();};
window.__gecitQuiet();
var ae=document.activeElement;if(editable(ae)){quiet(ae);send('Show',NAME);}
})();";

    public string startUrl;   // boş değilse açılışta bu adres yüklenir (yeni pencereler için)

    void Start()
    {
        if (!string.IsNullOrEmpty(startUrl)) SetInitialUrl(startUrl);
        container.browser.Init();
    }

    // Pencere açıkken başka bir siteye git; henüz başlamadıysa açılış adresi olarak ayarla
    public void OpenUrl(string url)
    {
        if (container.browser.IsInitialized()) container.browser.LoadUrl(url);
        else { startUrl = url; SetInitialUrl(url); }
        script = null;   // pencere adı değişmiş olabilir: betik yeniden hazırlansın
    }

    void SetInitialUrl(string url)
    {
        var f = typeof(TLab.WebView.Browser).GetField("m_url", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null) f.SetValue(container.browser, url);
    }

    void Update()
    {
        var browser = container.browser;
        browser.DispatchMessageQueue();
        if (Time.unscaledTime >= next)
        {
            next = Time.unscaledTime + 1f / (InView() ? updatesPerSecond : 3f);
            browser.UpdateFrame();
        }

        // Sayfa her değiştiğinde betik kaybolur; ara ara yeniden ekle (zaten varsa hiçbir şey yapmaz)
        if (Time.unscaledTime >= nextInject && browser.IsInitialized())
        {
            nextInject = Time.unscaledTime + 1.5f;
            if (script == null) script = Script.Replace("%NAME%", windowName) + "if(window.__gecitQuiet)window.__gecitQuiet();";
            browser.EvaluateJS(script);
        }

        if (Time.unscaledTime >= nextStatus)
        {
            nextStatus = Time.unscaledTime + 1f;
            var tex = view != null ? view.texture : null;
            Status = $"Tarayici: hazir={browser.IsInitialized()} goruntu={(tex != null ? tex.width + "x" + tex.height : "yok")} " +
                     $"acik={(view != null && view.isActiveAndEnabled)}";
        }
    }

    Transform head;

    // Pencere görüş alanında mı (bakış yönüne 60° içinde)
    bool InView()
    {
        if (head == null) { var cam = Camera.main; if (cam == null) return true; head = cam.transform; }
        var to = transform.position - head.position;
        return Vector3.Angle(head.forward, to) < 60f;
    }

    public void Type(string text)
    {
        container.browser.EvaluateJS("window.__gecitType&&window.__gecitType(" + JsString(text) + ")");
    }

    public void Backspace() => container.browser.KeyEvent(67);   // KEYCODE_DEL
    public void Enter() => container.browser.KeyEvent(66);       // KEYCODE_ENTER
    public void Blur() => container.browser.EvaluateJS("window.__gecitBlur&&window.__gecitBlur()");
    public void ToggleFullscreen() => container.browser.EvaluateJS("window.__gecitToggleFs&&window.__gecitToggleFs()");
    public void Back()
    {
        // Tam ekrandaysa önce ondan çık, değilse önceki sayfa
        container.browser.EvaluateJS("if(document.fullscreenElement){window.__gecitExitFs();}else{history.length>1?history.back():0;}");
    }
    public void Reload() => container.browser.EvaluateJS("location.reload()");

    static string JsString(string s)
    {
        var sb = new System.Text.StringBuilder("'");
        foreach (var ch in s)
        {
            if (ch == '\'' || ch == '\\') sb.Append('\\').Append(ch);
            else if (ch < 32 || ch > 126) sb.Append("\\u").Append(((int)ch).ToString("x4"));
            else sb.Append(ch);
        }
        return sb.Append('\'').ToString();
    }
}
