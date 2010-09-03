/* Yet Another Forum.net
 * Copyright (C) 2006-2010 Jaben Cargman
 * http://www.yetanotherforum.net/
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 */
namespace YAF.Classes.Core
{
  #region Using

  using System;
  using System.Collections.Generic;
  using System.Data;
  using System.Diagnostics;
  using System.Linq;
  using System.Web;

  using YAF.Classes.Data;
  using YAF.Classes.Extensions;
  using YAF.Classes.Utils;

  #endregion

  /// <summary>
  /// Class used for multi-step DB operations so they can be cached, etc.
  /// </summary>
  public class YafDBBroker
  {
    #region Public Methods

    /// <summary>
    /// The user lazy data.
    /// </summary>
    /// <param name="userID">
    /// The user ID.
    /// </param>
    /// <returns>
    /// </returns>
    public DataRow ActiveUserLazyData(object userID)
    {
      string key = YafCache.GetBoardCacheKey(String.Format(Constants.Cache.ActiveUserLazyData, userID));

      // get a row with user lazy data...
      return YafContext.Current.Cache.GetItem(
        key, 
        YafContext.Current.BoardSettings.ActiveUserLazyDataCacheTimeout, 
        () =>
        DB.user_lazydata(
          userID, 
          YafContext.Current.PageBoardID, 
          YafContext.Current.BoardSettings.AllowEmailSending, 
          YafContext.Current.BoardSettings.EnableBuddyList, 
          YafContext.Current.BoardSettings.AllowPrivateMessages, 
          YafContext.Current.BoardSettings.EnableAlbum, 
          YafContext.Current.BoardSettings.UseStyledNicks));
    }

    /// <summary>
    /// Adds the Thanks info to a dataTable
    /// </summary>
    /// <param name="dataRows">
    /// </param>
    public void AddThanksInfo(IEnumerable<DataRow> dataRows)
    {
      var messageIds = dataRows.Select(x => x.Field<int>("MessageID"));

      // Initialize the "IsthankedByUser" column.
      dataRows.ForEach(x => x["IsThankedByUser"] = false);

      // Initialize the "Thank Info" column.
      dataRows.ForEach(x => x["ThanksInfo"] = String.Empty);

      // Iterate through all the thanks relating to this topic and make appropriate
      // changes in columns.
      var allThanks = DB.MessageGetAllThanks(messageIds.ToDelimitedString(","));

      foreach (var thanks in allThanks.Where(t => t.FromUserID != null && t.FromUserID == YafContext.Current.PageUserID)
        )
      {
        foreach (var f in
          dataRows.Where(x => x.Field<int>("MessageID") == thanks.MessageID))
        {
          f["IsThankedByUser"] = "true";
          f.AcceptChanges();
        }
      }

      var thanksFieldNames = new[] { "ThanksFromUserNumber", "ThanksToUserNumber", "ThanksToUserPostsNumber" };

      foreach (DataRow postRow in dataRows)
      {
        var messageId = postRow.Field<int>("MessageID");

        postRow["MessageThanksNumber"] = allThanks.Where(t => t.FromUserID != null && t.MessageID == messageId).Count();

        var thanksFiltered = allThanks.Where(t => t.MessageID == messageId);

        if (thanksFiltered.Any())
        {
          var thanksItem = thanksFiltered.First();

          postRow["ThanksFromUserNumber"] = thanksItem.ThanksFromUserNumber ?? 0;
          postRow["ThanksToUserNumber"] = thanksItem.ThanksToUserNumber ?? 0;
          postRow["ThanksToUserPostsNumber"] = thanksItem.ThanksToUserPostsNumber ?? 0;
        }
        else
        {
          thanksFieldNames.ForEach(f => postRow[f] = 0);
        }

        // load all all thanks info into a special column...
        postRow["ThanksInfo"] =
          thanksFiltered.Where(t => t.FromUserID != null).Select(
            x => "{0}|{1}".FormatWith(x.FromUserID.Value, x.ThanksDate)).ToDelimitedString(",");

        postRow.AcceptChanges();
      }
    }

    /// <summary>
    /// Returns the layout of the board
    /// </summary>
    /// <param name="boardID">
    /// BoardID
    /// </param>
    /// <param name="userID">
    /// UserID
    /// </param>
    /// <param name="categoryID">
    /// CategoryID
    /// </param>
    /// <param name="parentID">
    /// ParentID
    /// </param>
    /// <returns>
    /// Returns board layout
    /// </returns>
    public DataSet BoardLayout(object boardID, object userID, object categoryID, object parentID)
    {
      if (categoryID != null && long.Parse(categoryID.ToString()) == 0)
      {
        categoryID = null;
      }

      using (var ds = new DataSet())
      {
        // get the cached version of forum moderators if it's valid
        string key = YafCache.GetBoardCacheKey(Constants.Cache.ForumModerators);

        var moderator = YafContext.Current.Cache.GetItem<DataTable>(
          key, YafContext.Current.BoardSettings.BoardModeratorsCacheTimeout, this.GetModerators);

        // insert it into this DataSet
        ds.Tables.Add(moderator.Copy());

        // get the Category Table
        key = YafCache.GetBoardCacheKey(Constants.Cache.ForumCategory);
        DataTable category = YafContext.Current.Cache.GetItem(
          key, 
          YafContext.Current.BoardSettings.BoardCategoriesCacheTimeout, 
          () =>
            {
              var catDt = DB.category_list(boardID, null);
              catDt.TableName = YafDBAccess.GetObjectName("Category");
              return catDt;
            });

        // add it to this dataset				
        ds.Tables.Add(category.Copy());

        if (categoryID != null)
        {
          // make sure this only has the category desired in the dataset
          foreach (DataRow row in ds.Tables[YafDBAccess.GetObjectName("Category")].Rows)
          {
            if (Convert.ToInt32(row["CategoryID"]) != Convert.ToInt32(categoryID))
            {
              // delete it...
              row.Delete();
            }
          }

          ds.Tables[YafDBAccess.GetObjectName("Category")].AcceptChanges();
        }

        DataTable forum = DB.forum_listread(boardID, userID, categoryID, parentID);
        forum.TableName = YafDBAccess.GetObjectName("Forum");
        ds.Tables.Add(forum.Copy());

        ds.Relations.Add(
          "FK_Forum_Category", 
          ds.Tables[YafDBAccess.GetObjectName("Category")].Columns["CategoryID"], 
          ds.Tables[YafDBAccess.GetObjectName("Forum")].Columns["CategoryID"], 
          false);
        ds.Relations.Add(
          "FK_Moderator_Forum", 
          ds.Tables[YafDBAccess.GetObjectName("Forum")].Columns["ForumID"], 
          ds.Tables[YafDBAccess.GetObjectName("Moderator")].Columns["ForumID"], 
          false);

        bool deletedCategory = false;

        // remove empty categories...
        foreach (DataRow row in ds.Tables[YafDBAccess.GetObjectName("Category")].Rows)
        {
          DataRow[] childRows = row.GetChildRows("FK_Forum_Category");

          if (childRows.Length == 0)
          {
            // remove this category...
            row.Delete();
            deletedCategory = true;
          }
        }

        if (deletedCategory)
        {
          ds.Tables[YafDBAccess.GetObjectName("Category")].AcceptChanges();
        }

        return ds;
      }
    }

    /// <summary>
    /// The favorite topic list.
    /// </summary>
    /// <param name="userID">
    /// The user ID.
    /// </param>
    /// <returns>
    /// </returns>
    public List<int> FavoriteTopicList(int userID)
    {
      string key = YafCache.GetBoardCacheKey(String.Format(Constants.Cache.FavoriteTopicList, userID));

      // stored in the user session...
      var favoriteTopicList = HttpContext.Current.Session[key] as List<int>;

      // was it in the cache?
      if (favoriteTopicList == null)
      {
        // get fresh values
        DataTable favoriteTopicListDt = DB.topic_favorite_list(userID);

        // convert to list...
        favoriteTopicList = favoriteTopicListDt.GetColumnAsList<int>("TopicID");

        // store it in the user session...
        HttpContext.Current.Session.Add(key, favoriteTopicList);
      }

      return favoriteTopicList;
    }


    /// <summary>
    /// The get active list.
    /// </summary>
    /// <param name="guests">
    /// The guests.
    /// </param>
    /// <param name="bots">
    /// The bots.
    /// </param>
    /// <returns>
    /// </returns>
    public DataTable GetActiveList(bool guests, bool crawlers)
    {
        return this.GetActiveList(YafContext.Current.BoardSettings.ActiveListTime, guests, crawlers);
    }

    /// <summary>
    /// The get active list.
    /// </summary>
    /// <param name="activeTime">
    /// The active time.
    /// </param>
    /// <param name="guests">
    /// The guests.
    /// </param>
    /// <returns>
    /// </returns>
    public DataTable GetActiveList(int activeTime, bool guests, bool crawlers)
    {
      return
        this.StyleTransformDataTable(
          DB.active_list(
            YafContext.Current.PageBoardID, guests, crawlers, activeTime, YafContext.Current.BoardSettings.UseStyledNicks));
    }

    /// <summary>
    /// The get all moderators.
    /// </summary>
    /// <returns>
    /// </returns>
    public List<SimpleModerator> GetAllModerators()
    {
      // get the cached version of forum moderators if it's valid
      string key = YafCache.GetBoardCacheKey(Constants.Cache.ForumModerators);

      var moderator = YafContext.Current.Cache.GetItem<DataTable>(
        key, YafContext.Current.BoardSettings.BoardModeratorsCacheTimeout, this.GetModerators);

      return
        moderator.ToListObject(
          (row) =>
          new SimpleModerator(
            Convert.ToInt64(row["ForumID"]), 
            Convert.ToInt64(row["ModeratorID"]), 
            row["ModeratorName"].ToString(), 
            SqlDataLayerConverter.VerifyBool(row["IsGroup"])));
    }

    /// <summary>
    /// Get a simple forum/topic listing.
    /// </summary>
    /// <param name="boardId">
    /// The board Id.
    /// </param>
    /// <param name="userId">
    /// The user Id.
    /// </param>
    /// <returns>
    /// </returns>
    public List<SimpleForum> GetSimpleForumTopic(int boardId, int userId, DateTime timeFrame, int maxCount)
    {
      var forumData =
        DB.forum_listall(boardId, userId).AsEnumerable().Select(
          x => new SimpleForum() { ForumID = x.Field<int>("ForumID"), Name = x.Field<string>("Forum") }).ToList();

      // get topics for all forums...
      foreach (var forum in forumData)
      {
        forum.Topics =
          DB.topic_list(forum.ForumID, userId, false, timeFrame, 0, maxCount, false).AsEnumerable().
            Select(
              x =>
              new SimpleTopic()
              {
                TopicID = x.Field<int>("TopicID"),
                CreatedDate = x.Field<DateTime>("Posted"),
                Subject = x.Field<string>("Subject"),
                StartedUserID = x.Field<int>("UserID"),
                StartedUserName = UserMembershipHelper.GetDisplayNameFromID(x.Field<int>("UserID")),
                Replies = x.Field<int>("Replies"),
                LastPostDate = x.Field<DateTime>("LastPosted"),
                LastUserID = x.Field<int>("LastUserID"),
                LastUserName = UserMembershipHelper.GetDisplayNameFromID(x.Field<int>("LastUserID")),
                LastMessageID = x.Field<int>("LastMessageID"),
                FirstMessage = x.Field<string>("FirstMessage"),
                LastMessage = DB.message_list(x.Field<int>("LastMessageID")).AsEnumerable().First().Field<string>("Message"),
                Forum = forum
              }).ToList();
      }

      return forumData;
    }

    /// <summary>
    /// The get latest topics.
    /// </summary>
    /// <param name="numberOfPosts">
    /// The number of posts.
    /// </param>
    /// <returns>
    /// </returns>
    public DataTable GetLatestTopics(int numberOfPosts)
    {
      return this.GetLatestTopics(numberOfPosts, YafContext.Current.PageUserID);
    }

    /// <summary>
    /// The get latest topics.
    /// </summary>
    /// <param name="numberOfPosts">
    /// The number of posts.
    /// </param>
    /// <param name="userId">
    /// The user id.
    /// </param>
    /// <returns>
    /// </returns>
    public DataTable GetLatestTopics(int numberOfPosts, int userId)
    {
      return this.GetLatestTopics(numberOfPosts, userId, "Style");
    }

    /// <summary>
    /// The get latest topics.
    /// </summary>
    /// <param name="numberOfPosts">
    /// The number of posts.
    /// </param>
    /// <param name="userId">
    /// The user id.
    /// </param>
    /// <param name="styleColumnNames">
    /// The style Column Names.
    /// </param>
    /// <returns>
    /// </returns>
    public DataTable GetLatestTopics(int numberOfPosts, int userId, params string[] styleColumnNames)
    {
      return
        this.StyleTransformDataTable(
          DB.topic_latest(
            YafContext.Current.PageBoardID, 
            numberOfPosts, 
            userId, 
            YafContext.Current.BoardSettings.UseStyledNicks, 
            YafContext.Current.BoardSettings.NoCountForumsInActiveDiscussions), 
          styleColumnNames);
    }

    /// <summary>
    /// The get moderators.
    /// </summary>
    /// <returns>
    /// </returns>
    public DataTable GetModerators()
    {
      DataTable moderator = DB.forum_moderators();
      moderator.TableName = YafDBAccess.GetObjectName("Moderator");

      return moderator;
    }

    /// <summary>
    /// Loads the message text into the paged data if "Message" and "MessageID" exists.
    /// </summary>
    /// <param name="dataRows">
    /// </param>
    public void LoadMessageText(IEnumerable<DataRow> dataRows)
    {
      var messageIds =
        dataRows.AsEnumerable().Where(x => x.Field<string>("Message").IsNotSet()).Select(x => x.Field<int>("MessageID"));

      var messageTextTable = DB.message_GetTextByIds(messageIds.ToDelimitedString(","));

      if (messageTextTable == null)
      {
        return;
      }

      // load them into the page data...
      foreach (var dataRow in dataRows)
      {
        // find the message id in the results...
        var message =
          messageTextTable.AsEnumerable().Where(x => x.Field<int>("MessageID") == dataRow.Field<int>("MessageID")).
            FirstOrDefault();

        if (message != null)
        {
          dataRow.BeginEdit();
          dataRow["Message"] = message.Field<string>("Message");
          dataRow.EndEdit();
        }
      }
    }

    /// <summary>
    /// The style transform func wrap.
    /// </summary>
    /// <param name="dt">
    /// The DateTable
    /// </param>
    /// <returns>
    /// The style transform wrap.
    /// </returns>
    public DataTable StyleTransformDataTable(DataTable dt)
    {
      if (YafContext.Current.BoardSettings.UseStyledNicks)
      {
        var styleTransform = new StyleTransform(YafContext.Current.Theme);
        styleTransform.DecodeStyleByTable(ref dt, true);
      }

      return dt;
    }

    /// <summary>
    /// The style transform func wrap.
    /// </summary>
    /// <param name="dt">
    /// The DateTable
    /// </param>
    /// <param name="styleColumns">
    /// Style columns names
    /// </param>
    /// <returns>
    /// The style transform wrap.
    /// </returns>
    public DataTable StyleTransformDataTable(DataTable dt, params string[] styleColumns)
    {
      if (YafContext.Current.BoardSettings.UseStyledNicks)
      {
        var styleTransform = new StyleTransform(YafContext.Current.Theme);
        styleTransform.DecodeStyleByTable(ref dt, true, styleColumns);
      }

      return dt;
    }

    /// <summary>
    /// The Buddy list for the user with the specified UserID.
    /// </summary>
    /// <param name="UserID">
    /// </param>
    /// <returns>
    /// </returns>
    public DataTable UserBuddyList(int UserID)
    {
      string key = YafCache.GetBoardCacheKey(String.Format(Constants.Cache.UserBuddies, UserID));
      DataTable buddyList = YafContext.Current.Cache.GetItem(key, 10, () => DB.buddy_list(UserID));
      return buddyList;
    }

    /// <summary>
    /// The user ignored list.
    /// </summary>
    /// <param name="userId">
    /// The user id.
    /// </param>
    /// <returns>
    /// </returns>
    public List<int> UserIgnoredList(int userId)
    {
      string key = YafCache.GetBoardCacheKey(String.Format(Constants.Cache.UserIgnoreList, userId));

      // stored in the user session...
      var userList = HttpContext.Current.Session[key] as List<int>;

      // was it in the cache?
      if (userList == null)
      {
        // get fresh values
        DataTable userListDt = DB.user_ignoredlist(userId);

        // convert to list...
        userList = userListDt.GetColumnAsList<int>("IgnoredUserID");

        // store it in the user session...
        HttpContext.Current.Session.Add(key, userList);
      }

      return userList;
    }

    /// <summary>
    /// The user medals.
    /// </summary>
    /// <param name="userId">
    /// The user id.
    /// </param>
    /// <returns>
    /// </returns>
    public DataTable UserMedals(int userId)
    {
      string key = YafCache.GetBoardCacheKey(String.Format(Constants.Cache.UserMedals, userId));

      // get the medals cached...
      DataTable dt = YafContext.Current.Cache.GetItem(key, 10, () => DB.user_listmedals(userId));

      return dt;
    }

    #endregion
  }
}