using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FreeSql;
using IGeekFan.FreeKit.Extras.Dto;
using IGeekFan.FreeKit.Extras.FreeSql;
using LinCms.Blog.Collections;
using LinCms.Blog.Tags;
using LinCms.Entities.Blog;
using LinCms.Exceptions;
using LinCms.Extensions;
using LinCms.Security;
using Qiniu.Util;

namespace LinCms.Blog.Comments;

/// <summary>
/// 收藏服务
/// </summary>
public class CollectionService : CrudAppService<Collection, CollectionDto, CollectionDto, Guid, CollectionSearchDto, CreateUpdateCollectionDto, CreateUpdateCollectionDto>, ICollectionService
{
    #region Constructor
    private readonly IAuditBaseRepository<Collection, Guid> _collectionRepsitory;
    private readonly IAuditBaseRepository<ArticleCollection> _artCollectionRepsitory;

    public CollectionService(IAuditBaseRepository<Collection, Guid> collectionRepsitory, IAuditBaseRepository<ArticleCollection> artCollectionRepsitory) : base(collectionRepsitory)
    {
        _collectionRepsitory = collectionRepsitory;
        _artCollectionRepsitory = artCollectionRepsitory;
    } 
    #endregion

    protected override ISelect<Collection> CreateFilteredQuery(CollectionSearchDto input)
    {
        return base.CreateFilteredQuery(input)
             .Where(r => r.CreateUserId == CurrentUser.FindUserId())
            .WhereIf(input.Name.IsNotNullOrEmpty(), r => r.Name.Contains(input.Name));
    }

    /// <inheritdoc/>
    [Transactional]
    public override async Task DeleteAsync(Guid collectionId)
    {
        await _artCollectionRepsitory.DeleteAsync(r => r.CollectionId == collectionId);
        await _collectionRepsitory.DeleteAsync(r => r.Id == collectionId);
    }
}