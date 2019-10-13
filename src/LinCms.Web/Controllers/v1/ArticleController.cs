﻿using System;
using System.Collections.Generic;
using System.Linq;
using AutoMapper;
using FreeSql;
using LinCms.Web.Models.v1.Articles;
using LinCms.Web.Services.Interfaces;
using LinCms.Zero.Aop;
using LinCms.Zero.Data;
using LinCms.Zero.Domain.Blog;
using LinCms.Zero.Exceptions;
using LinCms.Zero.Extensions;
using LinCms.Zero.Repositories;
using LinCms.Zero.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LinCms.Web.Controllers.v1
{
    [Route("v1/article")]
    [ApiController]
    [Authorize]
    public class ArticleController : ControllerBase
    {
        private readonly AuditBaseRepository<Article> _articleRepository;
        private readonly GuidRepository<TagArticle> _tagArticleRepository;

        private readonly IArticleService _articleService;
        private readonly IMapper _mapper;
        private readonly ICurrentUser _currentUser;
        private readonly IFreeSql _freeSql;
        public ArticleController(AuditBaseRepository<Article> articleRepository, IMapper mapper, ICurrentUser currentUser, GuidRepository<TagArticle> tagArticleRepository, IFreeSql freeSql, IArticleService articleService)
        {
            _articleRepository = articleRepository;
            _mapper = mapper;
            _currentUser = currentUser;
            _tagArticleRepository = tagArticleRepository;
            _freeSql = freeSql;
            _articleService = articleService;
        }

        [HttpDelete("{id}")]
        public ResultDto DeleteArticle(int id)
        {
            bool isCreateArticle = _articleRepository.Select.Any(r => r.Id == id && r.CreateUserId == _currentUser.Id);
            if (!isCreateArticle)
            {
                throw new LinCmsException("无法删除别人的随笔!");
            }
            _articleService.Delete(id);
            return ResultDto.Success();
        }

        [HttpDelete("cms/{id}")]
        [LinCmsAuthorize("删除随笔", "随笔")]
        public ResultDto Delete(int id)
        {
            _articleService.Delete(id);
            return ResultDto.Success();
        }

        /// <summary>
        /// 我所有的随笔
        /// </summary>
        /// <param name="searchDto"></param>
        /// <returns></returns>
        [HttpGet]
        public PagedResultDto<ArticleDto> Get([FromQuery]ArticleSearchDto searchDto)
        {
            var select = _articleRepository
                .Select
                .IncludeMany(r => r.Tags)
                .Where(r => r.CreateUserId == _currentUser.Id)
                .WhereIf(searchDto.Title.IsNotNullOrEmpty(),r=>r.Title.Contains(searchDto.Title))
                .OrderByDescending(r => r.IsStickie)
                .OrderByDescending(r => r.Id);

            var articles = select
            .ToPagerList(searchDto, out long totalCount)
            .Select(a =>
            {
                ArticleDto articleDto = _mapper.Map<ArticleDto>(a);
                return articleDto;
            })
            .ToList();

            return new PagedResultDto<ArticleDto>(articles, totalCount);
        }

        /// <summary>
        /// 得到最新的随笔
        /// </summary>
        /// <param name="searchDto"></param>
        /// <returns></returns>
        [HttpGet("all")]
        public PagedResultDto<ArticleDto> GetLastArticles([FromQuery]ArticleSearchDto searchDto)
        {
            var select = _articleRepository
                .Select
                .IncludeMany(r => r.Tags)
                .WhereIf(searchDto.ClassifyId.HasValue,r=>r.ClassifyId==searchDto.ClassifyId)
                .WhereIf(searchDto.Title.IsNotNullOrEmpty(), r => r.Title.Contains(searchDto.Title))
                .WhereIf(searchDto.TagId.HasValue,r=>r.Tags.Exists(u=>u.Id==searchDto.TagId))
                .OrderByDescending(r => r.Id);

            var articles = select
                .ToPagerList(searchDto, out long totalCount)
                .Select(a =>
                {
                    ArticleDto articleDto = _mapper.Map<ArticleDto>(a);
                    return articleDto;
                })
                .ToList();

            return new PagedResultDto<ArticleDto>(articles, totalCount);
        }

        /// <summary>
        /// 随笔详情
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet("{id}")]
        [AllowAnonymous]
        public ArticleDto Get(int id)
        {
            Article article = _articleRepository.Select.IncludeMany(r => r.Tags).Where(a => a.Id == id).ToOne();

            ArticleDto articleDto = _mapper.Map<ArticleDto>(article);

            articleDto.ThumbnailDisplay = _currentUser.GetFileUrl(article.Thumbnail);

            return articleDto;
        }

        [HttpPost]
        public ResultDto Post([FromBody] CreateUpdateArticleDto createArticle)
        {
            bool exist = _articleRepository.Select.Any(r => r.Title == createArticle.Title && r.CreateUserId == _currentUser.Id);
            if (exist)
            {
                throw new LinCmsException("您有一个同样标题的随笔");
            }

            Article article = _mapper.Map<Article>(createArticle);
            article.Archive = DateTime.Now.ToString("yyy年MM月");
            article.Author = _currentUser.UserName;
            article.Tags = new List<Tag>();
            foreach (var articleTagId in createArticle.TagIds)
            {
                article.Tags.Add(new Tag()
                {
                    Id = articleTagId,
                });
            }
            _articleRepository.Insert(article);

            return ResultDto.Success("新建随笔成功");
        }


        [HttpPut("{id}")]
        public ResultDto Put(int id, [FromBody] CreateUpdateArticleDto updateArticle)
        {
            Article article = _articleRepository.Select.Where(r => r.Id == id).ToOne();
            if (article.CreateUserId == _currentUser.Id)
            {
                throw new LinCmsException("您无权修改他人的随笔");
            }
            if (article == null)
            {
                throw new LinCmsException("没有找到相关随笔");
            }

            bool exist = _articleRepository.Select.Any(r => r.Title == updateArticle.Title && r.Id != id && r.CreateUserId == _currentUser.Id);
            if (exist)
            {
                throw new LinCmsException("您有一个同样标题的随笔");
            }

            //使用AutoMapper方法简化类与类之间的转换过程
            _mapper.Map(updateArticle, article);


            _articleRepository.Update(article);

            _tagArticleRepository.Delete(r => r.ArticleId == id);

            List<TagArticle> tagArticles = new List<TagArticle>();

            updateArticle.TagIds.ForEach(r => tagArticles.Add(new TagArticle()
            {
                ArticleId = id,
                TagId = r
            }));

            _tagArticleRepository.Insert(tagArticles);

            return ResultDto.Success("更新随笔成功");
        }

    }
}