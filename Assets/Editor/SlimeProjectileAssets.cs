using System;
using System.Collections.Generic;
using System.IO;
using EarthOL.BodyThrow;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    /// <summary>Builds the reference-inspired jelly alien as a fused organic mesh and samples existing projectile albedos.</summary>
    public static class SlimeProjectileAssets
    {
        const string Folder = "Assets/Resources/Throwables";
        struct Ellipsoid { public Vector3 center, radii; }
        static readonly Ellipsoid[] Forms =
        {
            Form(0,.052f,0,.079f,.073f,.060f),
            Form(0,-.041f,0,.050f,.058f,.041f),
            Form(-.049f,-.039f,0,.020f,.033f,.026f), Form(.049f,-.039f,0,.020f,.033f,.026f),
            Form(-.027f,-.096f,-.002f,.025f,.027f,.031f), Form(.027f,-.096f,-.002f,.025f,.027f,.031f),
            Form(-.055f,.113f,0,.013f,.030f,.013f), Form(.055f,.113f,0,.013f,.030f,.013f),
            Form(-.067f,.140f,0,.021f,.020f,.020f), Form(.067f,.140f,0,.021f,.020f,.020f)
        };

        [MenuItem("Tools/Earth OL/Build jelly alien and slime materials")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            var catalog = Resources.Load<BodyProjectileCatalog>("Throwables/Catalog");
            if (catalog == null) throw new InvalidOperationException("Missing projectile catalog");
            var slimeShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/PersonSlime.shader");
            var alienShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/AlienJelly.shader");
            bool shaderError = false;
            foreach (var shader in new[] { slimeShader, alienShader })
            {
                if (shader == null) { Debug.LogError("[SlimeAssets] Shader asset missing"); shaderError = true; continue; }
                shaderError |= ShaderUtil.ShaderHasError(shader);
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    Debug.Log("[SlimeAssets] Shader " + shader.name + " line=" + message.line + ": " + message.message);
            }
            if (shaderError)
                throw new InvalidOperationException("Jelly shaders must compile");
            SaveMaterial("Assets/Resources/PersonShowcase.mat", AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/PersonShowcasePhoto.shader"), Color.white);
            SaveMaterial(Folder + "/PersonSlime.mat", slimeShader, Color.white);
            var jelly = SaveMaterial(Folder + "/AlienJelly.mat", alienShader, new Color(.53f, .98f, .025f, .90f));
            var eye = SaveMaterial(Folder + "/AlienEyes.mat", Shader.Find("Standard"), new Color(.025f, .18f, .009f, 1f));
            eye.SetFloat("_Glossiness", .94f);

            Mesh generated = BuildFusedBody();
            string meshPath = Folder + "/AlienBody.asset";
            Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (savedMesh == null) { AssetDatabase.CreateAsset(generated, meshPath); savedMesh = generated; }
            else { EditorUtility.CopySerialized(generated, savedMesh); Object.DestroyImmediate(generated); }

            var root = new GameObject("Projectile_JellyAlien");
            try
            {
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                var body = new GameObject("Fused Jelly Alien Body", typeof(MeshFilter), typeof(MeshRenderer));
                body.transform.SetParent(visual.transform, false);
                body.GetComponent<MeshFilter>().sharedMesh = savedMesh;
                body.GetComponent<MeshRenderer>().sharedMaterial = jelly;
                Eye(visual.transform, "Left almond eye", new Vector3(-.033f, .051f, -.055f), -18f, eye);
                Eye(visual.transform, "Right almond eye", new Vector3(.033f, .051f, -.055f), 18f, eye);
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                { renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; }
                var rigidbody = root.AddComponent<Rigidbody>();
                rigidbody.isKinematic = true; rigidbody.useGravity = false;
                var box = root.AddComponent<BoxCollider>();
                box.center = savedMesh.bounds.center; box.size = savedMesh.bounds.size;
                root.AddComponent<BodyProjectile>();
                root.AddComponent<ProjectileImpactBounce>();
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/JellyAlien.prefab");
                Texture2D thumbnail = RenderViews(root);
                var entries = new List<BodyProjectileCatalog.Entry>(catalog.entries);
                var entry = entries.Find(value => value.key == "JellyAlien");
                if (entry == null) { entry = new BodyProjectileCatalog.Entry { key = "JellyAlien" }; entries.Add(entry); }
                entry.displayName = "5号 · 果冻外星人";
                entry.prefab = prefab.GetComponent<BodyProjectile>(); entry.preview = thumbnail;
                foreach (var choice in entries)
                {
                    choice.slimeColor = choice.key == "JellyAlien" ? new Color(.59f, .94f, .075f) : DominantColor(choice.prefab);
                    Debug.Log("[SlimeAssets] " + choice.key + " dominant=" + choice.slimeColor);
                }
                catalog.entries = entries.ToArray();
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                Debug.Log("[SlimeAssets] PASS: five 3D projectiles, organic alien mesh vertices=" + savedMesh.vertexCount + ", dominant albedo colors and previews.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Ellipsoid Form(float x,float y,float z,float rx,float ry,float rz) =>
            new Ellipsoid { center=new Vector3(x,y,z), radii=new Vector3(rx,ry,rz) };

        static float Distance(Vector3 point)
        {
            float distance = 100f;
            foreach (var form in Forms)
            {
                Vector3 p = point - form.center;
                float d = (new Vector3(p.x/form.radii.x,p.y/form.radii.y,p.z/form.radii.z).magnitude-1f) *
                    Mathf.Min(form.radii.x, Mathf.Min(form.radii.y,form.radii.z));
                float k = .009f;
                float h = Mathf.Clamp01(.5f + .5f * (d-distance)/k);
                distance = Mathf.Lerp(d,distance,h)-k*h*(1-h);
            }
            return distance;
        }

        static Mesh BuildFusedBody()
        {
            const int nx=38,ny=56,nz=28;
            Vector3 min=new Vector3(-.105f,-.135f,-.078f),size=new Vector3(.21f,.31f,.156f);
            int Id(int x,int y,int z) => (z*(ny+1)+y)*(nx+1)+x;
            var points = new Vector3[(nx+1)*(ny+1)*(nz+1)];
            var values = new float[points.Length];
            for(int z=0;z<=nz;z++)for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)
            {
                int id=Id(x,y,z);
                points[id]=min+Vector3.Scale(size,new Vector3(x/(float)nx,y/(float)ny,z/(float)nz));
                values[id]=Distance(points[id]);
            }
            int[,] tetra={{0,1,2,6},{0,2,3,6},{0,3,7,6},{0,7,4,6},{0,4,5,6},{0,5,1,6}};
            var vertices=new List<Vector3>(); var normals=new List<Vector3>(); var triangles=new List<int>();
            var edgeCache=new Dictionary<ulong,int>();
            var corners=new int[8]; var inside=new int[4]; var outside=new int[4];
            for(int z=0;z<nz;z++)for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
            {
                corners[0]=Id(x,y,z);corners[1]=Id(x+1,y,z);corners[2]=Id(x+1,y+1,z);corners[3]=Id(x,y+1,z);
                corners[4]=Id(x,y,z+1);corners[5]=Id(x+1,y,z+1);corners[6]=Id(x+1,y+1,z+1);corners[7]=Id(x,y+1,z+1);
                for(int t=0;t<6;t++)
                {
                    int ni=0,no=0;
                    for(int c=0;c<4;c++){int id=corners[tetra[t,c]];if(values[id]<0)inside[ni++]=id;else outside[no++]=id;}
                    if(ni==0||ni==4)continue;
                    if(ni==1)Triangle(Edge(inside[0],outside[0]),Edge(inside[0],outside[1]),Edge(inside[0],outside[2]));
                    else if(ni==3)Triangle(Edge(outside[0],inside[0]),Edge(outside[0],inside[1]),Edge(outside[0],inside[2]));
                    else
                    {
                        int a=Edge(inside[0],outside[0]),b=Edge(inside[0],outside[1]),c=Edge(inside[1],outside[0]),d=Edge(inside[1],outside[1]);
                        Triangle(a,b,c);Triangle(b,d,c);
                    }
                }
            }
            var mesh=new Mesh{name="Organic fused jelly alien"};
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            return mesh;

            int Edge(int a,int b)
            {
                ulong key=((ulong)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
                if(edgeCache.TryGetValue(key,out int index))return index;
                Vector3 p=Vector3.Lerp(points[a],points[b],values[a]/(values[a]-values[b]));
                const float e=.0003f;
                Vector3 n=new Vector3(Distance(p+Vector3.right*e)-Distance(p-Vector3.right*e),
                    Distance(p+Vector3.up*e)-Distance(p-Vector3.up*e),Distance(p+Vector3.forward*e)-Distance(p-Vector3.forward*e)).normalized;
                index=vertices.Count;vertices.Add(p);normals.Add(n);edgeCache[key]=index;return index;
            }
            void Triangle(int a,int b,int c)
            {
                if(Vector3.Dot(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]),normals[a]+normals[b]+normals[c])<0)
                {int swap=b;b=c;c=swap;}
                triangles.Add(a);triangles.Add(b);triangles.Add(c);
            }
        }

        static void Eye(Transform parent,string name,Vector3 position,float tilt,Material material)
        {
            var eye=GameObject.CreatePrimitive(PrimitiveType.Sphere);eye.name=name;eye.transform.SetParent(parent,false);
            eye.transform.localPosition=position;eye.transform.localRotation=Quaternion.Euler(0,0,tilt);
            eye.transform.localScale=new Vector3(.043f,.024f,.014f);
            Object.DestroyImmediate(eye.GetComponent<Collider>());
            eye.GetComponent<Renderer>().sharedMaterial=material;
        }

        static Material SaveMaterial(string path,Shader shader,Color color)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
            material.shader=shader;if(material.HasProperty("_Color"))material.color=color;
            EditorUtility.SetDirty(material);return material;
        }

        static Color DominantColor(BodyProjectile prefab)
        {
            var histogram=new float[512];var sums=new Vector3[512];
            foreach(var renderer in prefab.GetComponentsInChildren<Renderer>())
            foreach(var material in renderer.sharedMaterials)
            {
                if(material==null)continue;
                Color tint=material.HasProperty("_Color")?material.color:Color.white;
                var texture=material.mainTexture;
                if(texture==null){Accumulate(tint,64f);continue;}
                var target=RenderTexture.GetTemporary(32,32,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
                var previous=RenderTexture.active;
                var sample=new Texture2D(32,32,TextureFormat.RGBA32,false);
                try
                {
                    Graphics.Blit(texture,target);RenderTexture.active=target;
                    sample.ReadPixels(new Rect(0,0,32,32),0,0);sample.Apply();
                    foreach(var pixel in sample.GetPixels())Accumulate(pixel*tint,1f);
                }
                finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(sample);}
            }
            int best=0;for(int i=1;i<histogram.Length;i++)if(histogram[i]>histogram[best])best=i;
            Vector3 rgb=histogram[best]>0?sums[best]/histogram[best]:new Vector3(.55f,.8f,.2f);
            return new Color(rgb.x,rgb.y,rgb.z,1f);
            void Accumulate(Color color,float weight)
            {
                Color.RGBToHSV(color,out float h,out float s,out float v);
                if(color.a<.05f||v<.10f||s<.15f)return;
                int r=Mathf.Clamp((int)(color.r*8),0,7),g=Mathf.Clamp((int)(color.g*8),0,7),b=Mathf.Clamp((int)(color.b*8),0,7);
                int bin=(r*8+g)*8+b;histogram[bin]+=weight;sums[bin]+=new Vector3(color.r,color.g,color.b)*weight;
            }
        }

        static Texture2D RenderViews(GameObject root)
        {
            const int width=320,height=420;
            var sheet=new Texture2D(width*3,height,TextureFormat.RGBA32,false);
            var preview=new PreviewRenderUtility();
            try
            {
                var clone=Object.Instantiate(root);preview.AddSingleGO(clone);
                preview.camera.orthographic=true;preview.camera.orthographicSize=.17f;
                preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=5;
                preview.camera.clearFlags=CameraClearFlags.SolidColor;
                preview.camera.backgroundColor=new Color(.94f,.96f,.94f,1);
                preview.lights[0].intensity=1.4f;preview.lights[0].transform.rotation=Quaternion.Euler(30,-30,0);
                preview.lights[1].intensity=.7f;
                for(int view=0;view<3;view++)
                {
                    Vector3 direction=view==0?Vector3.back:view==1?Vector3.right:Vector3.forward;
                    preview.camera.transform.position=new Vector3(0,.02f,0)+direction;
                    preview.camera.transform.LookAt(new Vector3(0,.02f,0));
                    preview.BeginPreview(new Rect(0,0,width,height),GUIStyle.none);preview.camera.Render();
                    var rendered=preview.EndPreview();
                    var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32);
                    var previous=RenderTexture.active;
                    var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
                    try
                    {
                        Graphics.Blit(rendered,target);RenderTexture.active=target;
                        image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                        sheet.SetPixels(view*width,0,width,height,image.GetPixels());
                        if(view==0)File.WriteAllBytes(Folder+"/Previews/JellyAlien.png",image.EncodeToPNG());
                    }
                    finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(image);}
                }
                sheet.Apply();
                File.WriteAllBytes("Assets/BodyThrow/ArtPreviews/JellyAlienThreeViews.png",sheet.EncodeToPNG());
                AssetDatabase.Refresh();
                var importer=(TextureImporter)AssetImporter.GetAtPath(Folder+"/Previews/JellyAlien.png");
                importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Previews/JellyAlien.png");
            }
            finally{Object.DestroyImmediate(sheet);preview.Cleanup();}
        }
    }
}
