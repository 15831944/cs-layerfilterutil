﻿// (C) Copyright 2016 by CyberStudioApps.com / Jeff Stuyvesant

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.LayerManager;


// This line is not mandatory, but improves loading performances
[assembly: CommandClass(typeof(LayerFilterUtil.MyCommands))]

namespace LayerFilterUtil
{

	// This class is instantiated by AutoCAD for each document when
	// a command is called by the user the first time in the context
	// of a given document. In other words, non static data in this class
	// is implicitly per-document!
	public class MyCommands
	{
		// simple enum for type of compare
		enum Compare
		{
			String, Bool, Int
		}

		// inner class for the array of Criteria
		class CriteriaData
		{
			public readonly string Name1;
			public readonly string Name2;
			public string Name
			{
				get { return Name1 + " " + Name2; }
			}

			public readonly Compare CompareType;

			public string Operator { get; set; }
			public string Operand { get; set; }

			public CriteriaData(string Name1, String Name2, Compare CompareType)
			{
				this.Name1 = Name1;
				this.Name2 = Name2;
				this.CompareType = CompareType;
				Operator = "";
				Operand = "";
			}
		}


		// constants for the position of elements
		// in the argument passed
		private const int FUNCTION = 0;
		private const int FILTER_NAME = 1;
		private const int FILTER_CRITERIA = 1;
		private const int FILTER_TYPE = 2;
		private const int FILTER_PARENT = 3;
		private const int FILTER_EXPRESSION = 4;
		private const int FILTER_LAYERS = 4;
		private const int FILTER_MIN = 5;

		private const int CRIT_LAYERNAME = 0;
		private const int CRIT_PARENTNAME = 1;
		private const int CRIT_ISGROUP = 2;
		private const int CRIT_ALLOWDELETE = 3;
		private const int CRIT_ALLOWNESTED = 4;
		private const int CRIT_NESTCOUNT = 5;

		// constants for the position of elements
		// in the criteria list
		private static CriteriaData[] Criteria =
		{
			new CriteriaData("filter", "name", Compare.String),
			new CriteriaData("parent", "name", Compare.String),
			new CriteriaData("is", "group", Compare.Bool),
			new CriteriaData("allow", "delete", Compare.Bool),
			new CriteriaData("allow", "nested", Compare.Bool),
			new CriteriaData("nest", "count", Compare.Int)
		};

		private string CriteriaPatternAllTests = "("
			+ Criteria[CRIT_LAYERNAME].Name1 + "\\s" + Criteria[CRIT_LAYERNAME].Name2 + "|"
			+ Criteria[CRIT_PARENTNAME].Name1 + "\\s" + Criteria[CRIT_PARENTNAME].Name2 + "|"
			+ Criteria[CRIT_NESTCOUNT].Name1 + "\\s" + Criteria[CRIT_NESTCOUNT].Name2 + ")" +
			@"\s*(|=|==|<=|>=|!=|<|>)\s*(?!.*[\<\>\/\\\""\:\;\?\*\|\=\'].*)(.*)\b";


		private string CriteriaPatternBoolean = @"("
			+ Criteria[CRIT_ISGROUP].Name1 + "\\s" + Criteria[CRIT_ISGROUP].Name2 + "|"
			+ Criteria[CRIT_ALLOWDELETE].Name1 + "\\s" + Criteria[CRIT_ALLOWDELETE].Name2 + "|"
			+ Criteria[CRIT_ALLOWNESTED].Name1 + "\\s" + Criteria[CRIT_ALLOWNESTED].Name2 + ")" +
			@"\s*(|=|==)\s*(true|false|on|off|yes|no|1|0)\b";


		private static int nestDepth;

		private Document doc;
		private Database db;
		private Editor ed;


		[LispFunction("LayerFilterUtil")]

		public ResultBuffer LayerFilterUtil(ResultBuffer args) // This method can have any name
		{

			// initalize global vars
			doc = Application.DocumentManager.MdiActiveDocument;	// get reference to the current document
			db = doc.Database;										// get reference to the current dwg database
			ed = doc.Editor;										// get reference to the current editor (text window)

			// access to the collection of layer filters
			LayerFilterTree lfTree = db.LayerFilters;
			LayerFilterCollection lfCollect = lfTree.Root.NestedFilters;

			TypedValue[] tvArgs;

			// reset for each usage
			nestDepth = 0;

			// make sure that the criteria list is empty
			ClearCriteria();

			// process the args buffer
			// if passed is null, no good return null
			if (args != null)
			{
				// convert the args buffer into an array
				tvArgs = args.AsArray();

				// if the first argument is not text
				// return null
				if (tvArgs[FUNCTION].TypeCode != (int)LispDataType.Text)
				{
					return null;
				}
			}
			else
			{
				DisplayUsage();
				return null; ;
			}

			switch (((string)tvArgs[FUNCTION].Value).ToLower())
			{
				case "list":
					// bmk: List
					// validate the args buffer - there can be only a single argument
					if (tvArgs.Length == 1)
					{
						return ListFilters(lfCollect);
					}
					break;
				case "find":
					// bmk: Find
					// finding existing layer filter(s)

					return FindFilter(lfCollect, tvArgs);
				case "add":
					// bmk: Add
					// add a new layer filter to the layer filter collection
					// allow the filter to be added as a nested filter to another filter
					// except that any filter that cannot be deleted, cannot have nested filters
					// parameter options:
					// first	(idx = 0 FUNCTION) parameter == "add"
					// second	(idx = 1 FILTER_NAME) parameter == "filter name" (cannot be duplicate)
					// third	(idx = 2 FILTER_TYPE) parameter == "filter type" either "property" or "group" (case does not matter)
					// fifth	(idx = 3 FILTER_PARENT) parameter == "parent name" or ("" or nil) for no parent name
					// fourth	(idx = 4 FILTER_EXPRESSION) parameter == "filter expression" for property filter
					// fourth	(idx = 4 FILTER_LAYERS) parameter == "layer ids" for a group filter
					

					// possible add options:
					// add a property filter to the root of the collection
					// add a property filter to another layer filter (property or group)
					// add a group filter to the root of the collection
					// add a group filter to to another group filter (cannot be a property filter)

					return AddFilter(lfTree, lfCollect, tvArgs);
				case "delete":
					// bmk: Delete
					// deleting existing layer filter(s) - only 2 args allowed
					// special case filter to delete = "*" means all of the existing filters
					// except those filters marked as "cannot delete"
					// provided and is the 2nd arg a text arg?  if yes, proceed

					return DeleteFilter(lfTree, lfCollect, tvArgs);
				case "usage":
					// bmk: Usage
					DisplayUsage();
					break;
				case "version":
					// bmk: Version
					//ed.WriteMessage("LayerFilterUtil version: " + typeof(MyCommands).Assembly.GetName().Version);
					return new ResultBuffer(new TypedValue((int)LispDataType.Text,
						"LayerFilterUtil version: " + typeof(MyCommands).Assembly.GetName().Version));
				}

			return null;
		}

		/// <summary>
		/// List all of the layer filters
		/// </summary>
		/// <param name="lfCollect"></param>
		/// <returns></returns>
		private ResultBuffer ListFilters(LayerFilterCollection lfCollect)
		{
			ClearCriteria();

			return BuildResBuffer(SearchFilters(lfCollect));
		}

		/// <summary>
		/// Finds a filter based on the information provided:
		/// If only (2) args ("find" + layername) - return just the one filter
		/// If a list is provided ("find" + a list of search criteria) find all
		/// filters that match the criteria
		/// </summary>
		/// <param name="lfCollect"></param>
		/// <param name="tvArgs"></param>
		/// <returns></returns>
		private ResultBuffer FindFilter(LayerFilterCollection lfCollect, TypedValue[] tvArgs)
		{
			if (tvArgs.Length == 2)
			{
				if (tvArgs[FILTER_NAME].TypeCode == (int) LispDataType.Text)
				{
					ClearCriteria();

					Criteria[CRIT_LAYERNAME].Operator = "==";
					Criteria[CRIT_LAYERNAME].Operand = (string) tvArgs[FILTER_NAME].Value;

					List<LayerFilter> lFilters = SearchFilters(lfCollect);

					if (lFilters != null)
					{
						return BuildResBuffer(lFilters);
					}
				}
			}
			else
			{
				// more than 2 args - have a criteria list
				// parse the criteria list

				if (!GetCriteriaFromArgList(tvArgs))
				{
					return null;
				}

				List<LayerFilter> lFilters = SearchFilters(lfCollect);

				if (lFilters != null)
				{
					return BuildResBuffer(lFilters);
				}
			}

			return null;
		}


		/// <summary>
		/// Finds a single filter from the Layer Filter Collection<para />
		/// Returns a Result Buffer with the Layer Filter information
		/// </summary>
		/// <param name="lfCollect"></param>
		/// <param name="searchName"></param>
		/// <returns></returns>
		private ResultBuffer FindOneFilter(LayerFilterCollection lfCollect, string searchName)
		{
			ClearCriteria();

			Criteria[CRIT_LAYERNAME].Operator = "==";
			Criteria[CRIT_LAYERNAME].Operand = searchName;

			List<LayerFilter> lFilters = SearchFilters(lfCollect);

			if (lFilters.Count <= 0)
			{
				return null;
			}

			return BuildResBuffer(lFilters);
		}

		/// <summary>
		/// Add a filter
		/// </summary>
		/// <param name="lfTree"></param>
		/// <param name="lfCollect"></param>
		/// <returns></returns>
		private ResultBuffer AddFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect, TypedValue[] tvArgs)
		{
			// validate parameters
			// by getting to this point, first parameter validated

			// minimum of 5 parameters and 
			// parameters 1 & 2 must be text
			// parameter 3 must be text or nil
			// or new filter cannot already exist
			if (tvArgs.Length < FILTER_MIN
				|| tvArgs[FILTER_NAME].TypeCode != (int)LispDataType.Text
				|| tvArgs[FILTER_TYPE].TypeCode != (int)LispDataType.Text
				|| (tvArgs[FILTER_PARENT].TypeCode != (int)LispDataType.Text
				&& tvArgs[FILTER_PARENT].TypeCode != (int)LispDataType.Nil)
				|| SearchOneFilter(lfCollect, (string)tvArgs[FILTER_NAME].Value) != null)
			{
				return null;
			}

			// proceed based on filter type

			switch (((string)tvArgs[FILTER_TYPE].Value).ToLower())
			{
				// add a property filter
				case "property":
					// final parameter validation:
					// parameter count must be == FILTER_MIN
					// last parameter must be text
					if (tvArgs.Length != FILTER_MIN ||
						tvArgs[FILTER_EXPRESSION].TypeCode != (int)LispDataType.Text)
					{
						return null;
					}

					// two cases - add to root of tree or add to existing
					// if tvArgs[FILTER_PARENT] == "" or tvArgs[FILTER_PARENT] == nil, add to tree root
					// if tvArgs[FILTER_PARENT] == string, add to existing parent

					if (tvArgs[FILTER_PARENT].TypeCode == (int)LispDataType.Nil || ((string)tvArgs[FILTER_PARENT].Value).Length == 0)
					{
						// already checked that new filter does not exist - ok to proceed
						// add a property filter with the parent being null
						if (AddPropertyFilter(lfTree, lfCollect, (string)tvArgs[FILTER_NAME].Value, null, (string)tvArgs[FILTER_EXPRESSION].Value))
						{
							// filter added, return the data about the new filter
							return FindOneFilter(lfCollect, (string)tvArgs[FILTER_NAME].Value);
						}

					}
					else
					{
						// bit more complex - add a layer filter to an existing layer filter (nested layer filter)
						// parent filter must exist
						List<LayerFilter> lfList = SearchOneFilter(lfCollect, (string)tvArgs[FILTER_PARENT].Value);

						if (lfList != null)
						{
							// already checked that the new filter does not exist - ok to proceed
							// add a property filter using a parent
							if (AddPropertyFilter(lfTree, lfCollect, (string)tvArgs[FILTER_NAME].Value, lfList[0], (string)tvArgs[FILTER_EXPRESSION].Value))
							{
								// filter added, return data about the filter
								return FindOneFilter(lfCollect, (string)tvArgs[FILTER_NAME].Value);
							}
						}
					}

					// get here, something did not work - return nil
					return null;
				case "group":
					// final parameter validation:
					// parameter count must be >= FILTER_MIN (basic parameters + list begin + (1) layer + list end)
					// last parameter must be text
					if (tvArgs.Length < FILTER_MIN) { return null; }

					List<string> layerNames = GetLayersFromArgList(tvArgs);

					if (layerNames.Count == 0) { return null; }

					ObjectIdCollection layIds = GetLayerIds(layerNames);

					if (layIds.Count == 0) { return null;}

					// two cases - have or have not parent

					if (tvArgs[FILTER_PARENT].TypeCode == (int)LispDataType.Nil || ((string)tvArgs[FILTER_PARENT].Value).Length == 0)
					{
						// simple case - add group filter to the tree root

						// args at this point:
						// FUNCTION = "add" - already verified
						// FILTER_NAME = filter name - already verified
						// FILTER_TYPE = "group" - already verified
						// FILTER_PARENT = filter parent is blank or nil - already verified
						// FILTER_LAYERS = begining of the list of layers to include in the group filter

						if (AddGroupFilter(lfTree, lfCollect,
							(string)tvArgs[FILTER_NAME].Value, null, layIds))
						{
							return FindOneFilter(lfCollect, (string)tvArgs[FILTER_NAME].Value);
						}
						
						// provide the return information
						return null;
					}
					else
					{
						// complex case - add group filter to an existing filter
						// existing filter cannot be a property filter

						// args at this point:
						// FUNCTION = "add" - already verified
						// FILTER_NAME = filter name - already verified (data type)
						// FILTER_TYPE = "group" - already verified
						// FILTER_PARENT = filter parent is not blank - already verified
						// FILTER_LAYERS = begining of the list of layers to include in the group filter

						List<LayerFilter> lfList = SearchOneFilter(lfCollect, (string)tvArgs[FILTER_PARENT].Value);

						// now have a list of layer id's for the layer group
						// now add the layer filter group and its layer id's

						if (AddGroupFilter(lfTree, lfCollect,
							(string)tvArgs[FILTER_NAME].Value, lfList[0], layIds))
						{
							return FindOneFilter(lfCollect, (string)tvArgs[FILTER_NAME].Value);
						}
					
						// provide the return information
						return null;
					}
			}
			return null;
		}



		/// <summary>
		/// Add a property filter
		/// </summary>
		/// <param name="lfTree">Filter Tree</param>
		/// <param name="lfCollect">Filter Collection</param>
		/// <param name="Name">New Filter Name</param>
		/// <param name="Parent">Existing Parent under which to add the new filter (null if add to root</param>
		/// <param name="Expression">The filter expression</param>
		/// <returns></returns>
		private bool AddPropertyFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect, 
			string Name, LayerFilter Parent, string Expression) 
		{

			if (Parent != null && Parent.AllowNested != true)
			{
				return false;
			}

			// the layer filter collection is lfCollect
			// since this may cause an error, use try catch
			try
			{
				// make an empty layer filter
				LayerFilter lf = new LayerFilter();

				// add the layer filter data
				lf.Name = Name;
				lf.FilterExpression = Expression;


				if (Parent == null)
				{
					// add the filter to the collection
					lfCollect.Add(lf);
				} 
				else 
				{
					// add the layer filter as a nested filter
					Parent.NestedFilters.Add(lf);
				}

				// add the collection back to the data base
				db.LayerFilters = lfTree;

				// update the layer palette to show the
				// layer filter changes
				RefreshLayerManager();
			}
			catch (System.Exception)
			{
				// something did not work, return false
				return false;
			}

			return true;
		}

		/// <summary>
		/// Add a group filter
		/// </summary>
		/// <param name="lfTree"></param>
		/// <param name="lfCollect"></param>
		/// <param name="Name"></param>
		/// <param name="Parent"></param>
		/// <param name="layIds"></param>
		/// <returns></returns>
		private bool AddGroupFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect,
			string Name, LayerFilter Parent, ObjectIdCollection layIds )
		{
			// validate that this group filter is allowed to be added - 
			// if this is to be added to a Parent filter, the parent filter
			// must allow nesting
			// cannot be an ID (property) filter
			if (Parent != null && (Parent.AllowNested != true || Parent.IsIdFilter != true))
			{
				return false;
			}

			try
			{
				// create a blank layer filter group
				LayerGroup lg = new LayerGroup();

				// set its name
				lg.Name = Name;

				// add each layer id for the group
				foreach (ObjectId layId in layIds)
				{
					lg.LayerIds.Add(layId);
				}

				if (Parent == null)
				{
					// add the layer filter group to the collection
					lfCollect.Add(lg);
				}
				else
				{
					// add the layer filter as a nested filter
					Parent.NestedFilters.Add(lg);
				}

				// update the database with the updated tree
				db.LayerFilters = lfTree;

				// update the layer palette to show the
				// layer filter changes
				RefreshLayerManager();
			}
			catch (System.Exception)
			{
				return false;
			}
			return true;
		}



		/// <summary>
		/// Method to delete filters - either one or all
		/// </summary>
		/// <param name="lfTree"></param>
		/// <param name="lfCollect"></param>
		/// <param name="tvArgs"></param>
		/// <returns></returns>
		private ResultBuffer DeleteFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect, TypedValue[] tvArgs)
		{
			if (tvArgs.Length == 2)
			{
				// just a layer filter name provided
				if (tvArgs[FILTER_NAME].TypeCode == (int) LispDataType.Text)
				{
					string searchName = (string) tvArgs[FILTER_NAME].Value;
					nestDepth = 0;

					if (searchName != "*")
					{
						// create a list that should only be for the one filter based
						// on the name and that is may be deleted
						ClearCriteria();

						Criteria[CRIT_LAYERNAME].Operator = "==";
						Criteria[CRIT_LAYERNAME].Operand = searchName;

						Criteria[CRIT_ALLOWDELETE].Operator = "==";
						Criteria[CRIT_ALLOWDELETE].Operand = "true";

						List<LayerFilter> lFilters = SearchFilters(lfCollect);

						if (lFilters.Count == 1)
						{
							return DeleteListOfFilters(lfTree, lfCollect, lFilters);
						}
					}
					else
					{
						// special case, name to delete is *
						// delete all filters that are not marked as cannot delete

						ClearCriteria();

						Criteria[CRIT_ALLOWDELETE].Operator = "==";
						Criteria[CRIT_ALLOWDELETE].Operand = "true";

						List<LayerFilter> lfList = SearchFilters(lfCollect);

						if (lfList != null && lfList.Count > 0)
						{
							return DeleteListOfFilters(lfTree, lfCollect, lfList);
						}

					}
				}
			}
			else
			{
				// more than 2 args - using search criteria?
				//ed.WriteMessage("\n@1");
				// more than (2) args - a criteria list has been provided
				// get the critera from the arg list
				if (!GetCriteriaFromArgList(tvArgs)) { return null; }

				//ed.WriteMessage("\n@2\n");
				//DisplayCriteria();
				//ed.WriteMessage("\n@3");

				// now have the list of filters to delete
				List<LayerFilter> lFilters = SearchFilters(lfCollect);

				//ed.WriteMessage("\n@4 # of filters: " + lFilters.Count);

				if (lFilters.Count > 0)
				{
					//ed.WriteMessage("\n@5");
					return (DeleteListOfFilters(lfTree, lfCollect, lFilters));
				}
				//ed.WriteMessage("\n@9");
			}
			return null;
		}

		/// <summary>
		/// Delete the layer filter provided
		/// </summary>
		/// <param name="lfTree"></param>
		/// <param name="lfCollect"></param>
		/// <param name="lFilter"></param>
		/// <returns></returns>
		private bool DeleteOneFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect, LayerFilter lFilter)
		{
			// if the LayerFilter provided is null
			// or the filter is not allowed to be deleted
			// return null
			if (lFilter == null || !lFilter.AllowDelete)
			{
				return false;
			}

			// when several filters are being deleted and
			// because a parent filter can be deleted before
			// before the child (which would delete the child
			// automatically, must check whether he filter exists
			// return true (already deleted)
			if (SearchOneFilter(lfCollect, lFilter.Name) == null) { return true; }


			// does this LayerFilter have a parent?
			if (lFilter.Parent == null)
			{
				// no - remove the filter from the root collection
				lfCollect.Remove(lFilter);
			}
			else
			{
				// yes - remove the filter from the parent's collection
				lFilter.Parent.NestedFilters.Remove(lFilter);
			}

			// write the updated layer filter tree back to the database
			db.LayerFilters = lfTree;

			// return success
			return true;
		}

		/// <summary>
		/// Deletes all of the filters in the List provided
		/// </summary>
		/// <param name="lfTree"></param>
		/// <param name="lfCollect"></param>
		/// <param name="lFilters"></param>
		/// <returns></returns>
		private ResultBuffer DeleteListOfFilters(LayerFilterTree lfTree, LayerFilterCollection lfCollect, List<LayerFilter> lFilters)
		{
			// if no good info, return an empty ResultBuffer
			if (lFilters == null || lFilters.Count == 0) { return new ResultBuffer(); }

			foreach (LayerFilter lFilter in lFilters)
			{
				DeleteOneFilter(lfTree, lfCollect, lFilter);
			}
			// return the list of not deleted filters
				return ListFilters(lfCollect);
		}


		/// <summary>
		/// Display the usage message
		/// </summary>
		private void DisplayUsage()
		{
			ed.WriteMessage("\nUsage:");
			ed.WriteMessage("\n● Display Usage:");
			ed.WriteMessage("\n\t\t(layerFilterUtil) or");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"usage\")");

			ed.WriteMessage("\n● List all of the layer filters:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"list\")");

			ed.WriteMessage("\n● Find a layer filter:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"find\" \"FilterNameToFind\")");

			ed.WriteMessage("\n● Find a layer filter(s) based on criteria:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"find\" (list \"criteria\" \"criteria\"))");

			ed.WriteMessage("\n● Add a top level property filter:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"add\" \"FilterNameToAdd\" \"Property\" nil \"Expression\")");

			ed.WriteMessage("\n● Add a property filter to an existing filter:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"add\" \"FilterNameToAdd\" \"Property\" \"ExistingFilterName\" \"Expression\")");

			ed.WriteMessage("\n● Add a top level group filter:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"add\" \"FilterNameToAdd\" \"Group\" nil (list \"LayerName\" \"LayerName\"))");

			ed.WriteMessage("\n● Add a group filter to an existing filter:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"add\" \"FilterNameToAdd\" \"Group\" \"ExistingFilterName\" (list \"LayerName\" \"LayerName\"))");

			ed.WriteMessage("\n● Delete a layer filter:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"delete\" \"FilterNameToDelete\")");

			ed.WriteMessage("\n● Delete a layer filter(s) based on criteria:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"find\" (list \"criteria\" \"criteria\"))");

			ed.WriteMessage("\n● Delete all allowable layer filters:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"delete\" \"*\")");

			ed.WriteMessage("\n● The case of names does not matter except that, when a");
			ed.WriteMessage("\n\tfilter is added, the case of the name is used.");
			ed.WriteMessage("\n● Returns a list when sucessful or nil when unsucessful\n");

		}

		/// <summary>
		/// Build the ResultBuffer based on a List of LayerFilters
		/// </summary>
		/// <param name="lFilters"></param>
		/// <returns></returns>
		private ResultBuffer BuildResBuffer(List<LayerFilter> lFilters)
		{
			ResultBuffer resBuffer = new ResultBuffer();

			// if nothing in the filter list, retrun null
			if (lFilters.Count <= 0)
			{
				return null;
			}

			// start the list
			resBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			// add the item count
			resBuffer.Add(
				new TypedValue((int)LispDataType.Int16, lFilters.Count));

			// begin the list of lists
			resBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			// add each list item
			foreach (LayerFilter lFilter in lFilters)
			{
				AddFilterToResBuffer(lFilter, resBuffer);
			}

			// end the list of lists
			resBuffer.Add(new TypedValue((int)LispDataType.ListEnd));

			// end the whole list
			resBuffer.Add(new TypedValue((int)LispDataType.ListEnd));

			return resBuffer;
		}

		/// <summary>
		/// Add a dotted pair to a ResultBuffer
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="dxfCode"></param>
		/// <param name="Value"></param>
		/// <param name="ResBuffer"></param>
		private void AddDottedPairToResBuffer<T>(int dxfCode, T Value, ResultBuffer ResBuffer)
		{
			// start with a list begin
			ResBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			// add the DXF code
			ResBuffer.Add(new TypedValue((int)LispDataType.Int16, dxfCode));

			// add the dotted pair value depending on the
			// type of TypedValue
			switch (Type.GetTypeCode(typeof(T)))
			{
				case TypeCode.String:
					ResBuffer.Add(
						new TypedValue((int)LispDataType.Text, Value));
					break;
				case TypeCode.Int16:
					ResBuffer.Add(
						new TypedValue((int)LispDataType.Int16, Value));
					break;
				case TypeCode.Int32:
					ResBuffer.Add(
						new TypedValue((int)LispDataType.Int32, Value));
					break;
				case TypeCode.Empty:
					ResBuffer.Add(
						new TypedValue((int)LispDataType.Text, ""));
					break;
			}

			// terminate the dotted pair
			ResBuffer.Add(new TypedValue((int)LispDataType.DottedPair));
		}

		/// <summary>
		/// Add a dotted pair to a ResultBuffer (overload for boolean values)
		/// </summary>
		/// <param name="dxfCode"></param>
		/// <param name="Value"></param>
		/// <param name="ResBuffer"></param>
		private void AddDottedPairToResBuffer(int dxfCode, bool Value, ResultBuffer ResBuffer)
		{
			// make a standard dotted pair by converting the boolean
			// to a short
			AddDottedPairToResBuffer(dxfCode, (short) (Value ? 1 : 0), ResBuffer);
		}

		/// <summary>
		/// Adds a layer filter to the ResultBufffer as a List of Dotted Pairs
		/// </summary>
		/// <param name="lFilter">The LayerFilter to Add</param>
		/// <param name="ResBuffer">The ResultBuffer in which to add the LayerFilter List</param>
		private void AddFilterToResBuffer(LayerFilter lFilter, ResultBuffer ResBuffer)
		{
			// DXF codes for the dotted pairs
			const int FILTERNAMEDXF = 300;
			const int FILTEREXPDXF = 301;
			const int FILTERPARENTDXF = 302;
			const int FILTERLAYERSDXF = 303;

			const int FILTERDELFLGDXF = 290;
			const int FILTERNESTFLGDXF = 291;
			const int FILTERGRPFLGDXF = 292;

			const int FILTERNESTCNTDXF = 90;

			ResBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			// 
			AddDottedPairToResBuffer(FILTERNAMEDXF, lFilter.Name, ResBuffer);

			// add either the layer expression dotted pair
			// of the list of layers dotted pair
			if (!lFilter.IsIdFilter)
			{
				// add the filter expression to the result buffer
				AddDottedPairToResBuffer(FILTEREXPDXF, lFilter.FilterExpression, ResBuffer);
			}
			else
			{
				// add the list of layers to the result buffer
				StringBuilder sb = new StringBuilder();

				using (Transaction tr = db.TransactionManager.StartTransaction())
				{
					// allocate for LayerTableRecord
					LayerTableRecord ltRecord;

					// iterate through all Layer Id's in the filter
					foreach (ObjectId layId in ((LayerGroup)lFilter).LayerIds)
					{
						// based on the layer Id, the the LayerTableRecord
						ltRecord = tr.GetObject(layId, OpenMode.ForRead) as LayerTableRecord;

						// add the layer name to the list with a trailing '/'
						sb.Append(ltRecord.Name + "/");
					}
				}

				// if the list of found layers is empty, create a "blank" entry
				if (sb.Length == 0) { sb = new StringBuilder("/", 1); }

				// have the formatted list of layers, add the dotted pair
				AddDottedPairToResBuffer(FILTERLAYERSDXF, sb.ToString(), ResBuffer);
			}

			// add dotted pair for the allow delete flag
			AddDottedPairToResBuffer(FILTERDELFLGDXF, lFilter.AllowDelete, ResBuffer);

			// add dotted pair for the parent name
			AddDottedPairToResBuffer(FILTERPARENTDXF,
				lFilter.Parent != null ? lFilter.Parent.Name : "",ResBuffer);

			// add dotted pair for the is id filter flag
			AddDottedPairToResBuffer(FILTERGRPFLGDXF, lFilter.IsIdFilter, ResBuffer);	// true = group filter; false = property filter

			// add dotted pair for the allow nested flag
			AddDottedPairToResBuffer(FILTERNESTFLGDXF, lFilter.AllowNested, ResBuffer);

			// add dotted pair for the nested filter count
			AddDottedPairToResBuffer(FILTERNESTCNTDXF, lFilter.NestedFilters.Count, ResBuffer);

			ResBuffer.Add(new TypedValue((int)LispDataType.ListEnd));
		}

//		/// <summary>
//		/// Search for LayerFilters based on the criteria provided
//		/// </summary>
//		/// <param name="lfCollect"></param>
//		/// <param name="Name"></param>
//		/// <param name="Parent"></param>
//		/// <param name="allowDelete"></param>
//		/// <param name="isGroup"></param>
//		/// <param name="allowNested"></param>
//		/// <param name="nestCount"></param>
//		/// <returns></returns>
//		private List<LayerFilter> SearchFilters(LayerFilterCollection lfCollect, 
//			string Name = null, string Parent = null, 
//			bool? allowDelete = null, bool? isGroup = null, 
//			bool? allowNested = null, string nestCount = null)
//		{
//			// create the blank list (no elements - length == 0)
//			List<LayerFilter> lfList = new List<LayerFilter>();
//
//			if (lfCollect.Count == 0 || nestDepth > 100) { return lfList; }
//
//			// prevent from getting too deep
//			nestDepth++;
//
//			foreach (LayerFilter lFilter in lfCollect)
//			{
//				if (ValidateFilter(lFilter, Name, Parent, allowDelete, isGroup, allowNested, nestCount)) {
//					lfList.Add(lFilter);
//				}
//
//				if (lFilter.NestedFilters.Count != 0)
//				{
//					List<LayerFilter> lfListReturn = 
//						SearchFilters(lFilter.NestedFilters, Name, Parent, allowDelete, isGroup, allowNested, nestCount);
//
//					if (lfListReturn != null && lfListReturn.Count != 0)
//					{
//						lfList.AddRange(lfListReturn);
//					}
//				}
//			}
//
//			return lfList;
//		}

		private List<LayerFilter> SearchFilters(LayerFilterCollection lfCollect)
		{
			// create the blank list (no elements - length == 0)
			List<LayerFilter> lfList = new List<LayerFilter>();

			if (lfCollect.Count == 0 || nestDepth > 100) { return lfList; }

			// prevent from getting too deep
			nestDepth++;

			foreach (LayerFilter lFilter in lfCollect)
			{
				if (ValidateFilter(lFilter))
				{
					lfList.Add(lFilter);
				}

				if (lFilter.NestedFilters.Count != 0)
				{
					List<LayerFilter> lfListReturn =
						SearchFilters(lFilter.NestedFilters);

					if (lfListReturn != null && lfListReturn.Count != 0)
					{
						lfList.AddRange(lfListReturn);
					}
				}
			}

			return lfList;
		}

		/// <summary>
		/// Search for a filter of the name provided
		/// </summary>
		/// <param name="lfCollect"></param>
		/// <param name="searchName"></param>
		/// <returns></returns>
		private List<LayerFilter> SearchOneFilter(LayerFilterCollection lfCollect, string searchName)
		{
			ClearCriteria();

			Criteria[CRIT_LAYERNAME].Operator = "==";
			Criteria[CRIT_LAYERNAME].Operand = searchName;

			List<LayerFilter> lFilters = SearchFilters(lfCollect);

			return (lFilters.Count == 1 ? lFilters : null);

		} 

		/// <summary>
		/// Validate a filter against the Criteria array
		/// </summary>
		/// <param name="lFilter"></param>
		/// <returns></returns>
		private bool ValidateFilter(LayerFilter lFilter)
		{
			for (int i = 0; i < Criteria.Length; i++)
			{
				// if no operand, skip this test
				if (Criteria[i].Operand.Equals("")) { continue; }

				switch (Criteria[i].CompareType)
				{
					case Compare.String:
						switch (i)
						{
							case CRIT_LAYERNAME:
								if (!CompareMe(lFilter.Name, Criteria[i].Operator, Criteria[i].Operand)) { return false; }
								break;
							case CRIT_PARENTNAME:
								if (!CompareMe(lFilter.Parent.Name, Criteria[i].Operator, Criteria[i].Operand)) { return false; }
								break;
							default:
								return false;
						}
						break;
					case Compare.Int:
						int iValue;

						if (!int.TryParse(Criteria[i].Operand, out iValue)) { return false; }

						switch (i)
						{
							case CRIT_NESTCOUNT:
								if (!CompareMe(lFilter.NestedFilters.Count, Criteria[i].Operator, iValue)) { return false;  }
								break;
							default:
								return false;
						}
						break;
					case Compare.Bool:

						bool bValue;

						if (!bool.TryParse(Criteria[i].Operand, out bValue)) { return false; }

						switch (i)
						{
							case CRIT_ISGROUP:
								if (lFilter.IsIdFilter != bValue) { return false;}
								break;
							case CRIT_ALLOWDELETE:
								if (lFilter.AllowDelete != bValue) { return false; }
								break;
							case CRIT_ALLOWNESTED:
								if (lFilter.AllowNested != bValue) { return false; }
								break;
							default:
								return false;
						}

					break;
				}

			}

			return true;
		}
		
		/// <summary>
		/// Performs a comparison on two strings based on the operator provided
		/// </summary>
		/// <param name="Control">String to test</param>
		/// <param name="Operator">The type of comparison to preform</param>
		/// <param name="Test">String to test</param>
		/// <returns></returns>
		private bool CompareMe(string Control, string Operator, string Test)
		{
			Control = Control.ToLower();
			Test = Test.ToLower();

			switch (Operator)
			{
				case "":
				case "=":
				case "==":
					return Control.Equals(Test);
					break;
				case "<=":
					if (Control.Equals(Test)) { return true; }
					goto case "<";
				case "<":
					return String.Compare(Control, Test, StringComparison.OrdinalIgnoreCase) < 0 ? true : false;
					break;
				case ">=":
					if (Control.Equals(Test)) { return true; }
					goto case ">";
				case ">":
					return String.Compare(Control, Test, StringComparison.OrdinalIgnoreCase) > 0 ? true : false;
					break;
				case "!=":
					return !Control.Equals(Test);
					break;
			}
			return false;
		}

		/// <summary>
		/// Perform a comparison on two int's based on the operator provided
		/// </summary>
		/// <param name="Control">int to test</param>
		/// <param name="Operator">The type of comparison to preform</param>
		/// <param name="Test">int to test</param>
		/// <returns></returns>
		private bool CompareMe(int Control, string Operator, int Test)
		{
			switch (Operator)
			{
				case "":
				case "=":
				case "==":
					return Control == Test;
					break;
				case "<":
					return Control < Test;
					break;
				case "<=":
					return Control <= Test;
					break;
				case ">":
					return Control > Test;
					break;
				case ">=":
					return Control >= Test;
					break;
				case "!=":
					return Control != Test;
					break;
			}
			return false;
		}

		/// <summary>
		/// Clear any existing criteria information
		/// </summary>
		private void ClearCriteria()
		{
			foreach (CriteriaData item in Criteria) {
				item.Operator = "";
				item.Operand = "";
			}
		}


		int MatchCriteriaName(string SearchName)
		{
			for (int i = 0; i < Criteria.Length; i++)
			{
				if (Criteria[i].Name.Equals(SearchName))
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>
		/// Process the arg list for the search criteria
		/// return true if processed sucessfully
		/// return false if anything is worng
		/// </summary>
		/// <param name="tvArgs"></param>
		/// <returns></returns>
		bool GetCriteriaFromArgList(TypedValue[] tvArgs)
		{

			// eliminate any old data in the Criteria Array
			ClearCriteria();

			// if there are too few args (== no criteria), or
			// the TypeCode for the front / end of the list is wrong
			// return null;
			if (tvArgs.Length < FILTER_CRITERIA + 3 || 
				tvArgs[FILTER_CRITERIA].TypeCode != (int)LispDataType.ListBegin ||
				tvArgs[tvArgs.Length - 1].TypeCode != (int)LispDataType.ListEnd)
			{
				return false;
			}

			const int CRIT_TYPE = 1;
			const int CRIT_OPERATOR = 2;
			const int CRIT_VALUE = 3;

			int CriteriaIdx;
			string CriteriaOperator;
			string CriteriaValue;
			bool CriteriaBoolean;

			// run through the list and parse out the criteria
			for (int i = FILTER_CRITERIA + 1; i < tvArgs.Length - 1; i++)
			{
				// if any of the criteria passed is of the wrong type, whold list is invalid 
				// return an empty list
				if (tvArgs[i].TypeCode != (int)LispDataType.Text) { return false; }

				CriteriaBoolean = false;

				// got one criteria element - sub-divide
				Match m = Regex.Match((string)tvArgs[i].Value, CriteriaPatternAllTests, RegexOptions.IgnoreCase);

				// if the regular type of test failed, check for boolean form
				if (!m.Success)
				{
					m = Regex.Match((string)tvArgs[i].Value, CriteriaPatternBoolean, RegexOptions.IgnoreCase);
					CriteriaBoolean = true;
				}

				// if the boolean form failed, all done, return fail
				if (!m.Success) { return false;}

				// m.group[0] = whole match
				// m.group[1] = criteria type
				// m.group[2] = operator
				// m.group[3] = criteria value

				CriteriaIdx = MatchCriteriaName(m.Groups[CRIT_TYPE].Value.ToLower());

				if (CriteriaIdx < 0) { return false; }

				// preform some quick adjustments

				CriteriaOperator = m.Groups[CRIT_OPERATOR].Value.Equals("") ||
					m.Groups[CRIT_OPERATOR].Value.Equals("=") ? "==" :
					m.Groups[CRIT_OPERATOR].Value;

				CriteriaValue = m.Groups[CRIT_VALUE].Value;

				if (CriteriaBoolean)
				{
					CriteriaValue = CriteriaValue.ToLower();

					if (Regex.Match(CriteriaValue, @"(1|on|yes)").Success)
					{
						CriteriaValue = "true";
					} 
					else if (Regex.Match(CriteriaValue, @"(0|off|no)").Success)
					{
						CriteriaValue = "false";
					} 
				}

				Criteria[CriteriaIdx].Operator = CriteriaOperator;
				Criteria[CriteriaIdx].Operand = CriteriaValue;
			}

			return true;
		}

		/// <summary>
		/// Process the argument list and extract the layer names
		/// </summary>
		/// <param name="tvArgs"></param>
		/// <returns></returns>
		private List<string> GetLayersFromArgList(TypedValue[] tvArgs)
		{

			List<string> layerNames = new List<string>();

			// if there are too few args (== no layers), or
			// the TypeCode for the front / end of the list is wrong
			// return null
			if (tvArgs.Length < FILTER_LAYERS + 3  || 
				tvArgs[FILTER_LAYERS].TypeCode != (int)LispDataType.ListBegin ||
				tvArgs[tvArgs.Length - 1].TypeCode != (int)LispDataType.ListEnd)
			{
				return layerNames;
			}

			// the names are stored in an AutoCAD "list"
			// the first must be a list begin
			// followed by x number of text (layer names) entries
			// followed by a list end
			// validated above that there is a proper ListBegin and ListEnd elements

			// process the remainder of the elements and get the
			// layer names

			for (int i = FILTER_LAYERS + 1; i < tvArgs.Length - 1; i++)
			{
				// if one of the elements are bad, the whole list is considered bad
				if (tvArgs[i].TypeCode != (int)LispDataType.Text) { return new List<string>(); }

				// got a good name, add to the list
				layerNames.Add((string)tvArgs[i].Value);
			}

			return layerNames;

		} 


		/// <summary>
		/// Create a collection of Object Id's (LayerId's)
		/// </summary>
		/// <param name="layerNames">A List of layerNames</param>
		/// <returns>A collection of LayerId's</returns>
		private ObjectIdCollection GetLayerIds(List<string> layerNames)
		{
			ObjectIdCollection layIds = new ObjectIdCollection();

			if (layerNames.Count == 0)
			{
				return layIds;
			}

			// process the list of layers and place them into a sorted list
			// since working with a database item, use a transaction
			Transaction tr = db.TransactionManager.StartTransaction();

			using (tr)
			{
				LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

				foreach (ObjectId layId in lt)
				{
					LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(layId, OpenMode.ForRead);

					// check the name of the layer against the list of names to
					// add - if the key is contained, save the layer id and remove the
					// key from the add list
					if (layerNames.IndexOf(ltr.Name.ToLower()) >= 0)
					{
						layIds.Add(layId);
						layerNames.Remove(ltr.Name.ToLower());

						// if all of the layers get removed, done
						if (layerNames.Count == 0)
						{
							break;
						}
					}
				}

				tr.Commit();
			}

			return layIds;

		}

		/// <summary>
		/// Update the LayerManagerPalette so that the layer filter
		/// changes get displayed 
		/// </summary>
		private void RefreshLayerManager()
		{
			object manager = Application.GetSystemVariable("LAYERMANAGERSTATE");

			// force a refresh of the layermanager palette to have
			// the changes show up
			if (manager.ToString().Contains("1"))
			{
				doc.SendStringToExecute("layerpalette ", true, false, false);
			}

		}

#if DEBUG
		/// <summary>
		/// Display the data in the criteria array
		/// </summary>
		private void DisplayCriteria()
		{
			ed.WriteMessage("\nListing Criteria:");
			foreach (CriteriaData item in Criteria)
			{
				ed.WriteMessage("\nName: " + item.Name + " : operator: " + item.Operator + " : operand: " + item.Operand);
			}
		}

		/// <summary>
		/// List the information about the args passed to the command
		/// </summary>
		/// <param name="tvArgs">Array of args passed to the command</param>
		private void DisplayArgs(TypedValue[] tvArgs)
		{
			for (int i = 0; i < tvArgs.Length; i++)
			{
				ed.WriteMessage("arg#: " + i
					+ " : type: " + " \"" + DescribeLispDateType(tvArgs[i].TypeCode) + "\" (" + tvArgs[i].TypeCode + ")"
					+ " : value: >" + tvArgs[i].Value + "<");

				if (tvArgs[i].TypeCode == (short)LispDataType.Text)
				{
					ed.WriteMessage(" : length: " + ((string)tvArgs[i].Value).Length);
				}

				ed.WriteMessage("\n");

			}
		}

		/// <summary>
		/// Provide the description for the LispDataType
		/// </summary>
		/// <param name="tv">Short of the LispDataType</param>
		/// <returns></returns>
		private string DescribeLispDateType(short tv)
		{
			// todo - complete the below list
			switch (tv)
			{
				case (short)LispDataType.DottedPair:
					return "Dotted pair";
					break;
				case (short)LispDataType.Int16:
					return "Int16";
					break;
				case (short)LispDataType.Int32:
					return "Int32";
					break;
				case (short)LispDataType.ListBegin:
					return "ListBegin";
					break;
				case (short)LispDataType.ListEnd:
					return "ListEnd";
					break;
				case (short)LispDataType.None:
					return "None";
					break;
				case (short)LispDataType.Nil:
					return "Nil";
					break;
				case (short)LispDataType.Void:
					return "Dotted pair";
					break;
				case (short)LispDataType.Text:
					return "Text";
					break;
				default:
					return tv.ToString();

			}
		} 
#endif
	}
}
