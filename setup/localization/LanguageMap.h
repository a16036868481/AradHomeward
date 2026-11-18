// Copyright (c) 2026 eirros. All rights reserved.
#pragma once
#include <windows.h>
#include <algorithm>
#include <cstdint>
#include <fstream>
#include <mutex>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <vector>

inline bool FormatPublicTradePrices(std::wstring& text)
{
    bool changed=false;size_t at=0;
    auto letter=[](wchar_t c){return (c>=L'A'&&c<=L'Z')||(c>=L'a'&&c<=L'z');};
    while((at=text.find(L"gold/item",at))!=std::wstring::npos)
    {
        size_t end=at;while(end&&text[end-1]==L' ')--end;
        size_t first=end;
        while(first&&((text[first-1]>=L'0'&&text[first-1]<=L'9')||text[first-1]==L','||text[first-1]==L'.'))--first;
        if(first<end&&first&&letter(text[first-1])){text.insert(first,1,L' ');++at;changed=true;}
        size_t after=at+9;
        if(after<text.size()&&letter(text[after])){text.insert(after,1,L' ');++after;changed=true;}
        at=after;
    }
    return changed;
}

class LanguageMap
{
    std::unordered_map<std::wstring,std::wstring> rows;
    std::unordered_set<std::wstring> protectedNames;
    struct Segment {std::wstring source,target;bool name;};
    std::unordered_map<wchar_t,std::vector<Segment>> segments;
    std::unordered_map<wchar_t,std::vector<Segment>> marketSegments;
    static bool Read32(std::ifstream& file,uint32_t& value){return bool(file.read(reinterpret_cast<char*>(&value),4));}

    static std::wstring_view TrimLine(std::wstring_view line)
    {
        const auto first = line.find_first_not_of(L" \t");
        if (first == std::wstring_view::npos) return {};
        return line.substr(first, line.find_last_not_of(L" \t") - first + 1);
    }

    bool HasQuotedItem(std::wstring_view line, size_t afterBuy) const
    {
        const size_t start = line.find_first_not_of(L" \t", afterBuy);
        if (start == std::wstring_view::npos) return false;
        const auto items = marketSegments.find(line[start]);
        if (items == marketSegments.end()) return false;
        for (const auto& item : items->second)
        {
            if (item.source == L"收" || line.compare(start, item.source.size(), item.source) != 0) continue;
            const size_t number = line.find_first_not_of(L" \t", start + item.source.size());
            if (number != std::wstring_view::npos && line[number] >= L'0' && line[number] <= L'9') return true;
        }
        return false;
    }

    bool AppendLine(std::wstring_view line, std::wstring& result) const
    {
        const auto content = TrimLine(line);
        if (content.empty()) { result.append(line); return false; }
        const size_t separator = line.find_first_of(L":：");
        const bool hasSender = separator < 80;
        if (!protectedNames.empty() &&
            (protectedNames.count(std::wstring(content)) ||
             (hasSender && protectedNames.count(std::wstring(TrimLine(line.substr(0, separator)))))))
        {
            result.append(line);
            return false;
        }
        auto exact = rows.find(std::wstring(content));
        if (exact != rows.end() && exact->second != content)
        {
            const size_t first = content.data() - line.data();
            result.append(line.substr(0, first));
            result += exact->second;
            result.append(line.substr(first + content.size()));
            return true;
        }
        if ((segments.empty() && marketSegments.empty()) || line.find(L"sprite/") != std::wstring_view::npos ||
            line.find(L".img") != std::wstring_view::npos || line.find(L":\\") != std::wstring_view::npos)
        {
            result.append(line);
            return false;
        }

        const auto contains = [line](std::wstring_view text) { return line.find(text) != std::wstring_view::npos; };
        const bool priced = contains(L"金币/") || contains(L"金幣/") || contains(L"골드/") ||
                            contains(L"ゴールド/") || contains(L"gold/item");
        const bool market = priced || contains(L"集市购") || contains(L"旅伴集合【") || contains(L"同游【") ||
                            contains(L"邀你打【") || contains(L"沿途挑战【") || contains(L"旅人小队【") || contains(L"结伴去【");
        const size_t marketStart = market && hasSender ? separator + 1 : 0;
        const size_t quoteStart = priced ? line.find_first_not_of(L" \t", marketStart) : std::wstring_view::npos;
        // A price elsewhere in the line does not make words like “收拾” a buy quote.
        if (quoteStart != std::wstring_view::npos && line[quoteStart] == L'收' && !HasQuotedItem(line, quoteStart + 1))
        {
            result.append(line);
            return false;
        }
        const auto nameChar = [](wchar_t c) {
            return (c >= L'0' && c <= L'9') || (c >= L'A' && c <= L'Z') || (c >= L'a' && c <= L'z') ||
                   (c >= 0x3400 && c <= 0x9fff) || (c >= 0xac00 && c <= 0xd7af);
        };
        const auto latinWord = [](wchar_t c) {
            return (c >= L'0' && c <= L'9') || (c >= L'A' && c <= L'Z') || (c >= L'a' && c <= L'z');
        };

        std::wstring translated;
        bool changed = false, needsSpace = false;
        for (size_t at = 0; at < line.size();)
        {
            const Segment* match = nullptr;
            const auto select = [&](const auto& table, bool tradeTerms) {
                const auto candidates = table.find(line[at]);
                if (candidates == table.end()) return;
                for (const auto& item : candidates->second)
                {
                    const size_t size = item.source.size();
                    if ((match && size <= match->source.size()) || line.compare(at, size, item.source) != 0) continue;
                    if (item.name && ((at && nameChar(line[at - 1])) ||
                                     (at + size < line.size() && nameChar(line[at + size])))) continue;
                    // The single-character buy marker needs its own price line and an item followed by a price.
                    if (tradeTerms && item.source == L"收" && at != quoteStart) continue;
                    match = &item;
                    break;
                }
            };
            const bool tradeTerms = market && at >= marketStart;
            select(segments, false);
            if (tradeTerms) select(marketSegments, true);
            if (match)
            {
                if (tradeTerms && !translated.empty() && latinWord(translated.back()) && latinWord(match->target.front()))
                    translated.push_back(L' ');
                translated += match->target;
                at += match->source.size();
                changed = true;
                needsSpace = tradeTerms && latinWord(match->target.back());
            }
            else
            {
                if (needsSpace && latinWord(line[at])) translated.push_back(L' ');
                translated.push_back(line[at++]);
                needsSpace = false;
            }
        }
        if (priced) changed = FormatPublicTradePrices(translated) || changed;
        result += translated;
        return changed;
    }
public:
    bool LoadProtectedNames(const std::wstring& path)
    {
        std::ifstream file(path.c_str(),std::ios::binary);char magic[8];uint32_t count;
        if(!file.read(magic,8)||memcmp(magic,"ARNM\1\0\0\0",8)||!Read32(file,count)||count>4096)return false;
        std::unordered_set<std::wstring> pending;
        for(uint32_t i=0;i<count;++i)
        {
            uint32_t size;if(!Read32(file,size)||!size||size>128)return false;
            std::wstring name(size,L'\0');
            if(!file.read(reinterpret_cast<char*>(&name[0]),size*2)||name.find(L'\0')!=std::wstring::npos||!pending.emplace(std::move(name)).second)return false;
        }
        if(file.peek()!=std::char_traits<char>::eof())return false;
        protectedNames.swap(pending);return true;
    }
    bool Load(const std::wstring& path)
    {
        std::ifstream file(path.c_str(),std::ios::binary);char magic[8];uint32_t count;
        if(!file.read(magic,8)||memcmp(magic,"ARLP\1\0\0\0",8)||!Read32(file,count)||count>200000)return false;
        std::unordered_map<std::wstring,std::wstring> pending;pending.reserve(count);
        for(uint32_t i=0;i<count;++i)
        {
            uint32_t a,b;if(!Read32(file,a)||!Read32(file,b)||!a||!b||a>65536||b>65536)return false;
            std::wstring source(a,L'\0'),target(b,L'\0');
            if(!file.read(reinterpret_cast<char*>(&source[0]),a*2)||!file.read(reinterpret_cast<char*>(&target[0]),b*2)||source.find(L'\0')!=std::wstring::npos||target.find(L'\0')!=std::wstring::npos)return false;
            FormatPublicTradePrices(target);
            if(!pending.emplace(std::move(source),std::move(target)).second)return false;
        }
        if(file.peek()!=std::char_traits<char>::eof())return false;
        std::unordered_map<wchar_t,std::vector<Segment>> pendingSegments;
        std::unordered_map<wchar_t,std::vector<Segment>> pendingMarket;
        for(const auto& row:pending)
        {
            bool name=row.first.compare(0,6,L"@name:")==0;
            bool market=row.first.compare(0,8,L"@market:")==0;
            size_t skip=name?6:market?8:row.first.compare(0,9,L"@segment:")==0?9:0;
            if(!skip)continue;
            std::wstring source=row.first.substr(skip);
            if(source.empty()||source.size()>2048)return false;
            (market?pendingMarket:pendingSegments)[source[0]].push_back({std::move(source),row.second,name});
        }
        for(auto& item:pendingSegments)std::sort(item.second.begin(),item.second.end(),[](const Segment& a,const Segment& b){return a.source.size()>b.source.size();});
        for(auto& item:pendingMarket)std::sort(item.second.begin(),item.second.end(),[](const Segment& a,const Segment& b){return a.source.size()>b.source.size();});
        rows.swap(pending);segments.swap(pendingSegments);marketSegments.swap(pendingMarket);return true;
    }
    const wchar_t* Lookup(const wchar_t* source) const
    {
        if (!source || !*source) return source;
        const std::wstring key(source);
        if (protectedNames.count(key)) return source;
        const auto exact = rows.find(key);
        if (exact != rows.end()) return exact->second.c_str();
        if (key.size() > 8192) return source;

        // Each native text buffer can contain several independent chat lines.
        // Recognize price and sender context afresh for every line.
        const std::wstring_view text(key);
        std::wstring composed;
        bool changed = false;
        size_t start = 0;
        while (start < text.size())
        {
            size_t end = text.find_first_of(L"\r\n", start);
            if (end == std::wstring_view::npos) end = text.size();
            changed = AppendLine(text.substr(start, end - start), composed) || changed;
            if (end < text.size()) composed.push_back(text[end++]);
            start = end;
        }
        if (!changed) return source;
        static thread_local std::wstring buffers[10];
        static thread_local unsigned cursor = 0;
        std::wstring& output = buffers[cursor++ % 10];
        output = std::move(composed);
        return output.c_str();
    }
    size_t Count() const{return rows.size();}
};

// GetStr(int) returns persistent native interface strings. Its translated
// replacement must retain the same lifetime even after a map rehash or many
// later conversions that reuse thread-local conversion scratch buffers.
class NativeLanguageCache
{
    std::mutex guard;
    std::unordered_map<int,std::wstring> rows;
public:
    const wchar_t* Lookup(int id,const wchar_t* source,const LanguageMap& dictionary,bool& changed)
    {
        std::lock_guard<std::mutex> lock(guard);
        auto found=rows.find(id);
        if(found!=rows.end()){changed=true;return found->second.c_str();}
        const wchar_t* target=dictionary.Lookup(source);
        changed=target!=source;
        if(!changed)return source;
        return rows.emplace(id,target).first->second.c_str();
    }
};
