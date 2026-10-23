using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace MackieTranslator {
    public sealed class ApiService {
        public string Name,BaseUrl,Documentation,Note;
        public string[] Models,Hosts;
        public ApiService(string name,string url,string[] models,string[] hosts,string doc,string note){Name=name;BaseUrl=url;Models=models;Hosts=hosts;Documentation=doc;Note=note;}
        public bool AcceptsHost(string host){return Hosts.Contains(host,StringComparer.OrdinalIgnoreCase)||(Name=="通义千问"&&Regex.IsMatch(host,@"^[a-zA-Z0-9-]+\.cn-beijing\.maas\.aliyuncs\.com$"));}
    }
    public static class ApiServices {
        public static readonly ApiService[] All={
            new ApiService("DeepSeek","https://api.deepseek.com",new[]{"deepseek-flash","deepseek-v4-pro"},new[]{"api.deepseek.com"},"https://api-docs.deepseek.com/","使用 DeepSeek 官方 API 密钥。"),
            new ApiService("OpenAI","https://api.openai.com/v1",new[]{"gpt-4.1-mini","gpt-4.1","gpt-4o-mini"},new[]{"api.openai.com"},"https://developers.openai.com/api/docs/","使用 OpenAI 平台 API 密钥；模型可手动填写。"),
            new ApiService("Kimi","https://api.moonshot.cn/v1",new[]{"kimi-k2.6","kimi-k3"},new[]{"api.moonshot.cn","api.moonshot.ai"},"https://platform.kimi.com/docs/api/chat","使用 Kimi 开放平台密钥；部分模型始终进行思考。"),
            new ApiService("通义千问","https://dashscope.aliyuncs.com/compatible-mode/v1",new[]{"qwen-plus","qwen-flash","qwen-max"},new[]{"dashscope.aliyuncs.com","dashscope-intl.aliyuncs.com","dashscope-us.aliyuncs.com"},"https://help.aliyun.com/zh/model-studio/qwen-api-via-openai-chat-completions/","使用阿里云百炼 API 密钥，密钥地域需与地址一致；也可填写北京业务空间专属地址。"),
            new ApiService("智谱 GLM","https://open.bigmodel.cn/api/paas/v4",new[]{"glm-5.3","glm-5.2"},new[]{"open.bigmodel.cn"},"https://docs.bigmodel.cn/","使用智谱开放平台通用 API 密钥；模型以账户开通情况为准。"),
            new ApiService("豆包","https://ark.cn-beijing.volces.com/api/v3",new[]{"doubao-seed-2-1-pro-260915","doubao-seed-2-1-lite-260915"},new[]{"ark.cn-beijing.volces.com","ark.cn-beijing.volcengineapi.com"},"https://docs.volcengine.com/docs/ark/compatible-with-openai-sdk?lang=zh","使用火山方舟 API 密钥；模型栏可填已开通的模型 ID 或 ep- 接入点 ID。"),
            new ApiService("MiniMax","https://api.minimax.cn/v1",new[]{"MiniMax-M2.7","MiniMax-M3"},new[]{"api.minimax.cn","api.minimaxi.com","api.minimax.io"},"https://platform.minimax.cn/docs/api-reference/text-chat-openai","使用 MiniMax API 密钥；M2 系列始终思考，译文只显示最终结果。")
        };
        public static string[] Names {get{return new[]{"Bing"}.Concat(All.Select(x=>x.Name)).ToArray();}}
        public static ApiService Get(string name){return All.FirstOrDefault(x=>x.Name==name);}
        public static ApiService ForUrl(string value){return Uri.TryCreate(value,UriKind.Absolute,out Uri u)?All.FirstOrDefault(x=>x.AcceptsHost(u.Host)):null;}
        public static readonly string[] ApiFields={"base_url","full_url","model","ocr_model","economy_mode","streaming","temperature_enabled","temperature","max_tokens_enabled","max_tokens","reasoning_enabled","reasoning_effort"};
    }
}
