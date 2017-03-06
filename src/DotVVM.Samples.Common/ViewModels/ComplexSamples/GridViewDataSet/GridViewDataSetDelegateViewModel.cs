﻿using System;
using System.Collections.Generic;
using System.Linq;
using DotVVM.Framework.Controls;
using DotVVM.Framework.ViewModel;


namespace DotVVM.Samples.BasicSamples.ViewModels.ComplexSamples.GridViewDataSet
{
    public class GridViewDataSetDelegateViewModel : DotvvmViewModelBase
    {

        public int CallDelegateCounter { get; set; } = 0;

        public GridViewDataSetDelegateViewModel()
        {
            GridViewDataSet = new GridViewDataSet<Data>()
            {
                OnLoadingData = GetData,
                PagingOptions = new PagingOptions()
                {
                    PageSize = 5
                }
            };
        }


        public GridViewDataSet<Data> GridViewDataSet { get; set; } 

        public int ItemsCount { get; set; } = 20;

     

        private GridViewDataSetLoadedData<Data> GetData(IGridViewDataSetLoadOptions gridViewDataSetLoadOptions)
        {
            CallDelegateCounter++;

            var queryable = TestDB(ItemsCount);
            return queryable.GetDataFromQueryable(gridViewDataSetLoadOptions);
        }
      
        private IQueryable<Data> TestDB(int itemsCreatorCounter)
        {
            var dbdata = new List<Data>();
            for (var i = 0; i < itemsCreatorCounter; i++)
            {
                dbdata.Add(new Data
                {
                    Id = i,
                    Text = $"Item {i}"
                });
            }
            return dbdata.AsQueryable();
        }
    }
    public class Data
    {
        public int Id { get; set; }
        public string Text { get; set; }
    }
}